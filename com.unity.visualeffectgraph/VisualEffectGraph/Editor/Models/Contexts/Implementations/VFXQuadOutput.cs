using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.VFX;

namespace UnityEditor.VFX
{
    [VFXInfo]
    class VFXQuadOutput : VFXAbstractParticleOutput
    {
        [VFXSetting, SerializeField]
        protected FlipbookMode flipBook;

        public enum FlipbookMode
        {
            Off,
            Flipbook,
            FlipbookBlend,
        }

        public override string name { get { return "Quad Output"; } }
        public override string codeGeneratorTemplate { get { return "VFXShaders/VFXParticleQuad"; } }
        public override VFXTaskType taskType { get { return VFXTaskType.kParticleQuadOutput; } }

        public override IEnumerable<string> additionalDefines
        {
            get
            {
                foreach (var def in base.additionalDefines)
                    yield return def;

                if (flipBook != FlipbookMode.Off)
                {
                    yield return "USE_FLIPBOOK";
                    if (flipBook == FlipbookMode.FlipbookBlend)
                        yield return "USE_FLIPBOOK_INTERPOLATION";
                }
            }
        }

        public override IEnumerable<VFXAttributeInfo> attributes
        {
            get
            {
                yield return new VFXAttributeInfo(VFXAttribute.Position, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Color, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Alpha, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Size, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Alive, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Front, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Side, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Up, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Angle, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Pivot, VFXAttributeMode.Read);

                if (flipBook != FlipbookMode.Off)
                    yield return new VFXAttributeInfo(VFXAttribute.TexIndex, VFXAttributeMode.Read);
            }
        }

        protected override IEnumerable<VFXNamedExpression> CollectGPUExpressions(IEnumerable<VFXNamedExpression> slotExpressions)
        {
            foreach (var exp in base.CollectGPUExpressions(slotExpressions).Concat(slotExpressions.Where(o => o.name == "texture")))
                yield return exp;

            if (flipBook != FlipbookMode.Off)
            {
                var flipBookSizeExp = slotExpressions.First(o => o.name == "flipBookSize");
                yield return flipBookSizeExp;
                yield return new VFXNamedExpression(VFXValue.Constant(Vector2.one) / flipBookSizeExp.exp, "invFlipBookSize");
            }
        }

        protected override IEnumerable<VFXPropertyWithValue> inputProperties
        {
            get
            {
                foreach (var property in PropertiesFromType(GetInputPropertiesTypeName()))
                    yield return property;

                if (flipBook != FlipbookMode.Off)
                    yield return new VFXPropertyWithValue(new VFXProperty(typeof(Vector2), "flipBookSize"), Vector2.one);

                foreach (var property in base.inputProperties)
                    yield return property;
            }
        }

        protected override IEnumerable<string> filteredOutSettings
        {
            get
            {
                if (flipBook == FlipbookMode.Off)
                    yield return "frameInterpolationMode";
            }
        }

        public class InputProperties
        {
            public Texture2D texture;
        }
    }
}
