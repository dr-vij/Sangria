# SangriaMesh: подробная архитектура по файлам

Этот документ описывает `SangriaMesh` не как набор абстрактных идей, а как конкретную реализацию по каждому файлу в папке.

Основная цель `SangriaMesh`: разделить удобный для редактирования mesh-контейнер и быстрый для вычислений snapshot.

- `NativeDetail` — mutable слой для нодового authoring.
- `NativeCompiledDetail` — dense слой для Burst jobs / GPU-пайплайна.

Ниже каждый файл разобран отдельно: что делает, зачем нужен, как участвует в общем потоке данных.

## Общий поток данных в SangriaMesh

`NativeDetail` хранит разреженные домены (point/vertex/primitive), изменяемую топологию, доменные атрибуты и кастомные ресурсы. После серии изменений вызывается `Compile()`. На этапе компиляции SangriaMesh перестраивает структуру в плотный формат: линейные массивы индексов, packed-буферы атрибутов и packed-буфер ресурсов. Этот compiled-срез потребляется алгоритмами, где критичны линейный доступ к памяти, отсутствие постоянных hash-lookup в hot-path и минимальная стоимость ветвлений.

## Файлы контрактов и базовых типов

### `MeshDomain.cs`

В этом файле объявлен enum `MeshDomain` со значениями `Point`, `Vertex`, `Primitive`. На уровне кода это маленький тип, но архитектурно он фиксирует ключевой контракт SangriaMesh: любой атрибут и большинство API-операций должны работать относительно конкретного домена.

Файл нужен, чтобы домены были типизированы на уровне API, а не передавались строками или «магическими» числами. Это снижает шанс ошибок при вызовах вроде `TryGetAttributeAccessor` и делает вызовы безопаснее для рефакторинга.

### `CoreResult.cs`

Файл содержит enum статусов операций (`Success`, `AlreadyExists`, `NotFound`, `TypeMismatch`, `IndexOutOfRange`, `InvalidHandle`, `InvalidOperation`). SangriaMesh осознанно избегает исключений в обычном рабочем потоке и возвращает код результата почти во всех mutating/query API.

Зачем это нужно: модель рассчитана на массовые операции и jobs-ориентированный стиль, где исключения в hot-path нежелательны. Возврат кода результата позволяет дешево обрабатывать ошибки и сохранять предсказуемое поведение.

### `ElementHandle.cs`

`ElementHandle` — это стабильный идентификатор элемента (`Index + Generation`). `Index` может быть переиспользован, но `Generation` увеличивается при освобождении слота, из-за чего старый handle автоматически инвалидируется.

Файл нужен для корректной адресации элементов в mutable-среде нодового редактора. Если хранить только индекс, после удаления/добавления можно случайно попасть в «другой» элемент. `Generation` устраняет этот класс багов.

### `AttributeHandle.cs`

`AttributeHandle<T>` хранит `AttributeId`, внутренний `ColumnIndex` и `TypeHash`. Этот handle возвращается после resolve-операции в `AttributeStore` и затем используется для быстрого чтения/записи без повторного поиска атрибута по id.

Зачем файл нужен: это мост между удобным внешним API «по id» и быстрым внутренним путем «по колонке». После resolve код в циклах работает быстрее и с более явной типовой проверкой.

## Файлы доступа к данным атрибутов

### `NativeAttributeAccessor.cs`

Файл содержит `NativeAttributeAccessor<T>` — typed view поверх editable-буфера атрибута (`UnsafeList<byte>`). Он предоставляет индексатор, `GetRefUnchecked`, `GetPointerUnchecked`, `GetBasePointer`.

Нужен для двух сценариев: массовая обработка данных в mutable-слое и низкоуровневый доступ без постоянных вызовов `TryGet/TrySet`. Это уменьшает overhead в tight loops и оставляет контроль над безопасностью у вызывающего кода.

### `CompiledAttributeAccessor.cs`

`CompiledAttributeAccessor<T>` делает то же самое для compiled-данных, но работает поверх «голого» указателя на packed-blob. Здесь нет зависимости от mutable-контейнеров, только от base pointer + stride + count.

Этот файл нужен, чтобы compiled-срез можно было читать максимально дешево: минимум слоев абстракции, линейная адресация, хороший кандидат для Burst-инлайнинга.

## Файлы дескрипторов compiled-слоя

### `CompiledAttributeDescriptor.cs`

В файле описана структура метаданных compiled-атрибута: `AttributeId`, `TypeHash`, `Stride`, `Count`, `OffsetBytes`. Она говорит, где именно лежит колонка в общем packed-буфере и как ее интерпретировать.

Файл нужен, потому что compiled-данные хранятся «плотно» в одном blob. Без дескриптора невозможно безопасно и типизированно найти колонку.

### `CompiledResourceDescriptor.cs`

Аналогичный descriptor для ресурсов: `ResourceId`, `TypeHash`, `SizeBytes`, `OffsetBytes`.

Файл нужен для той же причины: ресурсы в compiled-срезе складываются в один буфер и требуют метаданных, чтобы извлекать payload по id и типу.

## Файлы контейнеров compiled-слоя

### `CompiledAttributeSet.cs`

`CompiledAttributeSet` хранит три компонента: массив дескрипторов, map `attributeId -> descriptorIndex`, и общий `NativeArray<byte>` с packed-данными. Метод `TryGetAccessor<T>` проверяет тип и возвращает `CompiledAttributeAccessor<T>`.

Файл нужен как единая точка доступа к compiled-атрибутам домена. Он инкапсулирует layout буфера и скрывает от клиентского кода детали упаковки.

Практический смысл: любой job получает компактный и предсказуемый набор данных, без mutable-колонок и без дополнительных структурных зависимостей.

### `CompiledResourceSet.cs`

`CompiledResourceSet` организован похожим образом, но для detail-level ресурсов. Он хранит descriptors, id-map и packed data blob; `TryGetResource<T>` возвращает typed значение.

Файл нужен, чтобы ресурсы жили по тем же правилам, что и атрибуты compiled-слоя: компактная память, простая сериализуемость/копируемость, быстрый read-path.

### `NativeCompiledDetail.cs`

Это итоговый compiled-срез. Он включает:

- `VertexToPointDense`
- `PrimitiveOffsetsDense`
- `PrimitiveVerticesDense`
- `PointAttributes`, `VertexAttributes`, `PrimitiveAttributes`
- `Resources`

Главный API-фокус файла — дать единый read-only контекст для вычислений. Через `TryGetAttributeAccessor` вызывающий код получает typed доступ к нужному домену, через `TryGetResource` — к packed-ресурсам.

Файл нужен, чтобы отделить runtime-представление от mutable-редактирования: compiled-снимок можно передать в обработку как стабильный набор данных без зависимости от внутреннего состояния `NativeDetail`.

## Файлы mutable-слоя: индексы, топология, атрибуты, ресурсы

### `SparseHandleSet.cs`

`SparseHandleSet` отвечает за жизненный цикл индексов домена. Внутри:

- `NativeBitArray m_Alive` — жив ли индекс
- `NativeList<uint> m_Generations` — поколение каждого слота
- `NativeList<int> m_FreeIndices` — free-list для реюза
- счетчики capacity/count/nextUnused

Алгоритм `Allocate` берет индекс из free-list либо расширяет диапазон, возвращая `ElementHandle` с валидной generation. `Release` помечает слот как dead, увеличивает generation и кладет индекс обратно в free-list.

Зачем файл нужен: это фундамент стабильных ссылок в mutable-модели. Без него `NativeDetail` пришлось бы выбирать между дорогим compaction в authoring-режиме и небезопасными raw-index ссылками.

### `PrimitiveStorage.cs`

Этот файл реализует хранилище переменной длины для primitive->vertices в mutable-слое. Каждый примитив имеет `PrimitiveRecord` (`Start`, `Length`, `Capacity`), а все данные лежат в одном большом `NativeList<int>`.

Когда у примитива не хватает capacity, `EnsureRecordCapacity` релокирует его сегмент в хвост общего буфера с удвоением размера. `SetVertices`, `AppendVertex`, `RemoveVertexAt` дают базовые мутации.

Зачем нужен отдельный файл: хранение переменной кардинальности примитивов — отдельная задача с собственной логикой аллокаций/релокаций. Выделение этой логики упрощает `NativeDetail` и дает пространство для будущих оптимизаций именно топологического storage.

### `AttributeStore.cs`

`AttributeStore` — mutable store колонок атрибутов одного домена. Ключевые части:

- `NativeList<AttributeColumn> m_Columns`
- `NativeParallelHashMap<int, int> m_IdToColumn`
- `m_ElementCapacity`

`RegisterAttribute<T>` создает новую колонку (байтовый буфер + stride + typeHash). `TryResolveHandle<T>` проверяет тип и возвращает `AttributeHandle<T>`. `TrySet/TryGet` работают через handle и индекс элемента. `EnsureCapacity` расширяет все колонки сразу, сохраняя доменную синхронизацию по размеру.

Отдельно важен `ClearElement`: при реюзе индексов SangriaMesh обнуляет значения всех колонок для нового элемента, чтобы не получать «грязные» данные от предыдущего владельца индекса.

Зачем файл нужен: это центральный механизм кастомных атрибутов в mutable-режиме. Он обеспечивает доменную гибкость и типовую проверку, не привязывая систему к фиксированному набору каналов.

### `ResourceRegistry.cs`

`ResourceRegistry` хранит custom resources detail-уровня как map `resourceId -> ResourceEntry`, где entry содержит raw payload, typeHash и размер. Это не доменные атрибуты; это отдельный канал для «метаданных/параметров/контекста».

`SetResource<T>` обновляет либо создает payload с проверкой типа. `TryGetResource<T>` типобезопасно читает ресурс. `Compile` упаковывает все ресурсы в `CompiledResourceSet`.

Зачем файл нужен: нодовые системы часто требуют хранить не только per-element данные, но и данные уровня узла/детали. Разделение resources и attributes делает модель чище и упрощает API.

## Главный orchestrator mutable-слоя

### `NativeDetail.cs`

Это основной файл SangriaMesh и точка сборки всей архитектуры.

`NativeDetail` владеет:

- тремя `SparseHandleSet` для point/vertex/primitive
- `m_VertexToPoint`
- `PrimitiveStorage`
- тремя `AttributeStore` по доменам
- `ResourceRegistry`
- версиями `TopologyVersion` и `AttributeVersion`

В конструкторе регистрируется обязательный `Position`-атрибут в point-домене.

#### Что делает в части API

В файле реализованы все публичные мутации и запросы:

- регистрация/удаление/наличие атрибутов
- resolve typed handles и доступ к accessor
- set/get доменных атрибутов
- set/get/remove custom resources
- add/remove/query для point/vertex/primitive
- компиляция в `NativeCompiledDetail`

#### Ключевые поведенческие решения

1. При добавлении новых элементов SangriaMesh может переиспользовать индекс; перед использованием индекс очищается (`ClearElement`) в соответствующем `AttributeStore`.
2. При удалении `Point` удаляются все `Vertex`, которые на него ссылаются.
3. При удалении `Vertex` он удаляется из всех примитивов; примитивы с менее чем 3 вершинами удаляются.
4. `Compile()` всегда строит новый плотный snapshot по живым элементам.

#### Что делает `Compile()` внутри

- собирает alive-индексы всех доменов
- строит remap `sparse -> dense`
- формирует `VertexToPointDense`
- формирует CSR-подобную пару `PrimitiveOffsetsDense + PrimitiveVerticesDense`
- копирует доменные атрибуты в packed blobs через внутренний `CompileAttributes`
- компилирует ресурсы через `ResourceRegistry.Compile`

Зачем файл нужен: это единый «core API surface» нового ядра. Все остальные файлы — специализированные блоки, а `NativeDetail` оркестрирует их в целостную модель данных.

## Как файлы связаны между собой

Связь можно читать как pipeline.

Сначала файлы контрактов (`MeshDomain`, `CoreResult`, `ElementHandle`, `AttributeHandle`) задают базовую типовую рамку. На нее опираются контейнеры mutable-слоя (`SparseHandleSet`, `PrimitiveStorage`, `AttributeStore`, `ResourceRegistry`). Поверх контейнеров строится `NativeDetail`, который реализует пользовательский API редактирования. После вызова `Compile()` включаются дескрипторы и accessor’ы compiled-слоя (`Compiled*Descriptor`, `Compiled*Accessor`, `Compiled*Set`), а финальным фасадом становится `NativeCompiledDetail`.

Именно это разделение на маленькие файлы и явные роли делает SangriaMesh расширяемым: можно улучшать layout compiled-буферов, добавлять домены или вводить инкрементальную компиляцию без полного пересмотра всего API.

## Почему структура разнесена именно так

Если бы все было в одном монолитном файле, гибкость модификаций быстро бы падала: изменение жизненного цикла handle-ов, упаковки атрибутов и ресурсо-канала конфликтовало бы между собой. В текущем разбиении каждый файл имеет узкую ответственность и понятный контракт на вход/выход.

Это важно для вашего сценария «узловой редактор + кастомные данные + дальнейший compute/gpu pipeline»: authoring-часть и runtime-часть эволюционируют с разной скоростью и разными требованиями к производительности, а file-level архитектура SangriaMesh это явно отражает.

## Документационные файлы в папке SangriaMesh

### `README.md`

`README.md` выполняет роль короткой входной точки для разработчика: что такое SangriaMesh, где основной подробный документ и в каком порядке обычно используется API. Этот файл специально держится компактным, чтобы быстро сориентировать нового читателя.

### `ARCHITECTURE.md`

Этот файл — расширенная инженерная спецификация текущего состояния SangriaMesh. Его цель — зафиксировать фактическое поведение по каждому исходнику, чтобы при эволюции ядра можно было сверять дизайн-решения и поддерживать общую картину системы без «знаний в голове» отдельных разработчиков.
