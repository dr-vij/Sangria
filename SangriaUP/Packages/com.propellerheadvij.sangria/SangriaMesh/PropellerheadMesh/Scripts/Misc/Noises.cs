using Unity.Burst;
using Unity.Mathematics;

namespace Legacy.Misc
{
    [BurstCompile]
    public static class Noises
    {
        private static readonly int[] PermutationTable = new int[512]
        {
            151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
            140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
            247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
            57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
            74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
            60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
            65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
            200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
            52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
            207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
            119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
            129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
            218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
            81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
            184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
            222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
            // Duplicate the first 256 values to avoid modulo operation
            151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
            140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
            247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
            57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
            74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
            60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
            65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
            200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
            52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
            207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
            119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
            129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
            218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
            81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
            184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
            222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180
        };

        [BurstCompile]
        public static int GetPermutation(int index)
        {
            return PermutationTable[index & 255];
        }

        [BurstCompile]
        public static float Fade(float t)
        {
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        [BurstCompile]
        public static float Grad1D(int hash, float x)
        {
            return (hash & 1) == 0 ? x : -x;
        }

        [BurstCompile]
        public static float Grad2D(int hash, float x, float y)
        {
            return ((hash & 1) == 0 ? x : -x) + ((hash & 2) == 0 ? y : -y);
        }

        [BurstCompile]
        public static float Grad3D(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        // Base noise functions with float parameters only
        [BurstCompile]
        public static float Noise1D(float x)
        {
            int X = (int)math.floor(x) & 255;
            x -= math.floor(x);
            float u = Fade(x);
            return math.lerp(Grad1D(GetPermutation(X), x), Grad1D(GetPermutation(X + 1), x - 1), u);
        }

        [BurstCompile]
        public static float Noise2D(float x, float y)
        {
            int X = (int)math.floor(x) & 255;
            int Y = (int)math.floor(y) & 255;
            x -= math.floor(x);
            y -= math.floor(y);
            float u = Fade(x);
            float v = Fade(y);
            int A = GetPermutation(X) + Y;
            int B = GetPermutation(X + 1) + Y;
            return math.lerp(
                math.lerp(Grad2D(GetPermutation(A), x, y), Grad2D(GetPermutation(B), x - 1, y), u),
                math.lerp(Grad2D(GetPermutation(A + 1), x, y - 1), Grad2D(GetPermutation(B + 1), x - 1, y - 1), u), v);
        }

        [BurstCompile]
        public static float Noise3D(float x, float y, float z)
        {
            int X = (int)math.floor(x) & 255;
            int Y = (int)math.floor(y) & 255;
            int Z = (int)math.floor(z) & 255;
            x -= math.floor(x);
            y -= math.floor(y);
            z -= math.floor(z);
            float u = Fade(x);
            float v = Fade(y);
            float w = Fade(z);
            int a = GetPermutation(X) + Y;
            int aa = GetPermutation(a) + Z;
            int ab = GetPermutation(a + 1) + Z;
            int b = GetPermutation(X + 1) + Y;
            int ba = GetPermutation(b) + Z;
            int bb = GetPermutation(b + 1) + Z;

            return math.lerp(
                math.lerp(
                    math.lerp(Grad3D(GetPermutation(aa), x, y, z), Grad3D(GetPermutation(ba), x - 1, y, z), u),
                    math.lerp(Grad3D(GetPermutation(ab), x, y - 1, z), Grad3D(GetPermutation(bb), x - 1, y - 1, z), u), v),
                math.lerp(
                    math.lerp(Grad3D(GetPermutation(aa + 1), x, y, z - 1), Grad3D(GetPermutation(ba + 1), x - 1, y, z - 1), u),
                    math.lerp(Grad3D(GetPermutation(ab + 1), x, y - 1, z - 1), Grad3D(GetPermutation(bb + 1), x - 1, y - 1, z - 1), u), v), w);
        }

        // Wrapper functions for struct parameters (non-Burst)
        public static float Noise2D(float2 pos) => Noise2D(pos.x, pos.y);
        public static float Noise3D(float3 pos) => Noise3D(pos.x, pos.y, pos.z);

        // Octave noise functions with float parameters only
        [BurstCompile]
        public static float OctaveNoise2D(float x, float y, int octaves, float persistence, float scale)
        {
            float total = 0;
            float frequency = scale;
            float amplitude = 1;
            float maxValue = 0;

            for (int i = 0; i < octaves; i++)
            {
                total += Noise2D(x * frequency, y * frequency) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= 2;
            }

            return total / maxValue;
        }

        [BurstCompile]
        public static float OctaveNoise3D(float x, float y, float z, int octaves, float persistence, float scale)
        {
            float total = 0;
            float frequency = scale;
            float amplitude = 1;
            float maxValue = 0;

            for (int i = 0; i < octaves; i++)
            {
                total += Noise3D(x * frequency, y * frequency, z * frequency) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= 2;
            }

            return total / maxValue;
        }

        // Wrapper functions for struct parameters (non-Burst)
        public static float OctaveNoise2D(float2 pos, int octaves, float persistence, float scale) => 
            OctaveNoise2D(pos.x, pos.y, octaves, persistence, scale);
        public static float OctaveNoise3D(float3 pos, int octaves, float persistence, float scale) => 
            OctaveNoise3D(pos.x, pos.y, pos.z, octaves, persistence, scale);

        // FBM Noise functions with float parameters only
        [BurstCompile]
        public static float FbmNoise2D(float x, float y, int octaves, float lacunarity, float gain)
        {
            float total = 0;
            float frequency = 1;
            float amplitude = 1;

            for (int i = 0; i < octaves; i++)
            {
                total += Noise2D(x * frequency, y * frequency) * amplitude;
                frequency *= lacunarity;
                amplitude *= gain;
            }

            return total;
        }

        [BurstCompile]
        public static float FbmNoise3D(float x, float y, float z, int octaves, float lacunarity, float gain)
        {
            float total = 0;
            float frequency = 1;
            float amplitude = 1;

            for (int i = 0; i < octaves; i++)
            {
                total += Noise3D(x * frequency, y * frequency, z * frequency) * amplitude;
                frequency *= lacunarity;
                amplitude *= gain;
            }

            return total;
        }

        // Wrapper functions for struct parameters (non-Burst)
        public static float FbmNoise2D(float2 pos, int octaves, float lacunarity, float gain) => 
            FbmNoise2D(pos.x, pos.y, octaves, lacunarity, gain);
        public static float FbmNoise3D(float3 pos, int octaves, float lacunarity, float gain) => 
            FbmNoise3D(pos.x, pos.y, pos.z, octaves, lacunarity, gain);

        // Ridged Noise functions with float parameters only
        [BurstCompile]
        public static float RidgedNoise2D(float x, float y, int octaves, float lacunarity, float gain)
        {
            float total = 0;
            float frequency = 1;
            float amplitude = 1;

            for (int i = 0; i < octaves; i++)
            {
                float n = math.abs(Noise2D(x * frequency, y * frequency));
                n = 1.0f - n;
                n *= n;
                total += n * amplitude;
                frequency *= lacunarity;
                amplitude *= gain;
            }

            return total;
        }

        // Wrapper function for struct parameters (non-Burst)
        public static float RidgedNoise2D(float2 pos, int octaves, float lacunarity, float gain) => 
            RidgedNoise2D(pos.x, pos.y, octaves, lacunarity, gain);

        
        // Turbulence Noise functions with float parameters only
        [BurstCompile]
        public static float TurbulenceNoise2D(float x, float y, int octaves, float lacunarity, float gain)
        {
            float total = 0;
            float frequency = 1;
            float amplitude = 1;

            for (int i = 0; i < octaves; i++)
            {
                total += math.abs(Noise2D(x * frequency, y * frequency)) * amplitude;
                frequency *= lacunarity;
                amplitude *= gain;
            }

            return total;
        }

        // Wrapper function for struct parameters (non-Burst)
        public static float TurbulenceNoise2D(float2 pos, int octaves, float lacunarity, float gain) => 
            TurbulenceNoise2D(pos.x, pos.y, octaves, lacunarity, gain);
    }
}