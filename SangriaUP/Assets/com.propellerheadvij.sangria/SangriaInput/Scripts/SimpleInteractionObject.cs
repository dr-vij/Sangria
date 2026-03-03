using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sangria.Input
{
    public class SimpleInteractionObject : InteractionObjectBase
    {
        public override IGestureAnalyzer CreateAnalyzer(Camera cam) => new SimpleGestureAnalyzer(this, cam, true);
    }
}