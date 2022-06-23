using System;
using UnityEditor.ShaderGraph.GraphDelta;
using UnityEngine;
using UnityEngine.GraphToolsFoundation.Overdrive;

namespace UnityEditor.ShaderGraph.GraphUI
{
    public class GradientTypeConstant : BaseShaderGraphConstant
    {
        protected override void StoreValue()
        {
            storedValue = (Gradient)GetValue();
        }

        public override object GetStoredValue()
        {
            return storedValue;
        }

        [SerializeField]
        Gradient storedValue;

        override protected object GetValue() => GradientTypeHelpers.GetGradient(GetField());
        override protected void SetValue(object value) => GradientTypeHelpers.SetGradient(GetField(), (Gradient)value);
        override public object DefaultValue => Activator.CreateInstance(Type);
        override public Type Type => typeof(Gradient);
        override public TypeHandle GetTypeHandle() => ShaderGraphExampleTypes.GradientTypeHandle;
    }

}
