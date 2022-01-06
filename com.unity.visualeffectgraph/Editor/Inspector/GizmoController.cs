using System;
using System.Linq;
using System.Collections;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.VFX;

namespace UnityEditor.VFX.UI
{
    [Flags]
    enum GizmoError
    {
        None = 0,
        HasLinkGPU = 1 << 0,
        NeedComponent = 1 << 1,
        NeedExplicitSpace = 1 << 2,
        NotAvailable = 1 << 3
    }

    interface IGizmoable
    {
        string name { get; }
    }

    interface IGizmoController
    {
        void DrawGizmos(VisualEffect component);
        Bounds GetGizmoBounds(VisualEffect component);
        GizmoError GetGizmoError(VisualEffect component);

        ReadOnlyCollection<IGizmoable> gizmoables { get; }
        IGizmoable currentGizmoable { get; set; }
    }
}
