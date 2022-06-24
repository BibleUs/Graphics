using System.Collections.Generic;
using System.Linq;
using UnityEditor.GraphToolsFoundation.Overdrive;
using UnityEditor.ShaderGraph.GraphDelta;
using UnityEngine;

namespace UnityEditor.ShaderGraph.GraphUI
{
    public class GraphDataVariableSettingsInspector : SGFieldsInspector
    {
        public GraphDataVariableSettingsInspector(string name, IModel model, IModelView ownerElement, string parentClassName)
            : base(name, model, ownerElement, parentClassName) { }

        protected override IEnumerable<BaseModelPropertyField> GetFields()
        {
            if (m_Model is not GraphDataVariableDeclarationModel variableDeclarationModel) yield break;

            if (variableDeclarationModel.IsExposable)
            {
                yield return new SGModelPropertyField<ContextEntryEnumTags.DataSource>(m_OwnerElement.RootView,
                    m_Model,
                    nameof(GraphDataVariableDeclarationModel.ShaderDeclaration),
                    "Shader Declaration",
                    null,
                    typeof(SetShaderDeclarationCommand));
            }
        }

        public override bool IsEmpty() => !GetFields().Any();
    }
}
