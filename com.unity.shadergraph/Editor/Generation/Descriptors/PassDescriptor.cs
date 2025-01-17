﻿namespace UnityEditor.ShaderGraph
{
    [GenerationAPI]
    internal struct PassDescriptor
    {
        // Definition
        public string displayName;
        public string referenceName;
        public string lightMode;
        public bool useInPreview;

        // Port mask
        public int[] vertexPorts;
        public int[] pixelPorts;

        // Collections
        public StructCollection structs;
        public FieldCollection requiredFields;
        public DependencyCollection fieldDependencies;
        public RenderStateCollection renderStates;
        public PragmaCollection pragmas;
        public DefineCollection defines;
        public KeywordCollection keywords;
        public IncludeCollection includes;

        // Custom Template
        public string passTemplatePath;
        public string sharedTemplateDirectory;

        // Methods
        public bool Equals(PassDescriptor other)
        {
            return referenceName == other.referenceName;
        }
    }
}
