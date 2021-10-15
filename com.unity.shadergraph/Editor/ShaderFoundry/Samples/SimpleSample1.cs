using System.Collections.Generic;

namespace UnityEditor.ShaderFoundry
{
    internal static class SimpleSample1
    {
        // Menu item to make it easier to run and output the test to a file
        //[MenuItem("Test/FoundrySimpleTest")]
        internal static void RunSimpleTest()
        {
            const string ShaderName = "SimpleSample1";
            // To build a shader, you need to provide a container and a target.
            // The shader code will be in the shader builder passed in.
            var target = SimpleSampleBuilder.GetTarget();
            var container = new ShaderContainer();
            var shaderBuilder = new ShaderBuilder();
            SimpleSampleBuilder.Build(container, target, ShaderName, BuildSample, shaderBuilder);

            var code = shaderBuilder.ToString();
            DisplayTestResult(ShaderName, code);
        }

        internal static void BuildSample(ShaderContainer container, CustomizationPoint vertexCP, CustomizationPoint surfaceCP, out CustomizationPointDescriptor vertexCPDesc, out CustomizationPointDescriptor surfaceCPDesc)
        {
            // This sample overrides only the SurfaceDescription customization point.
            // This CP is composed of three blocks for an example of how blocks can be composed and
            // how input/output names can be overridden. You can just as easily only create one block to start.

            // Currently don't provide any blocks for the vertex customization point.
            vertexCPDesc = CustomizationPointDescriptor.Invalid;

            // Build the blocks we're going to use.

            // GlobalsProvider redirects the global _TimeParameters into an available input
            var globalsProviderBlock = BuildGlobalsProviderBlock(container);
            // UvScroll scrolls 'uv' by time and a scroll speed property
            var uvScrollBlock = BuildUvScrollBlock(container);
            // AlbedoColor outputs a color variable from sampling a texture and color property
            var albedoColorBlock = BuildAlbedoColorBlock(container);

            // Now build the descriptors for each block. Blocks can be re-used multiple times within a shader.
            // The block descriptors add any unique data about the call. Currently there is no unique data,
            // but plans for manually re-mapping data between blocks is under way.
            var globalsProviderBlockDesc = SimpleSampleBuilder.BuildSimpleBlockDescriptor(container, globalsProviderBlock);
            var uvScrollBlockDesc = SimpleSampleBuilder.BuildSimpleBlockDescriptor(container, uvScrollBlock);
            var albedoColorBlockDesc = SimpleSampleBuilder.BuildSimpleBlockDescriptor(container, albedoColorBlock);

            // The order of these block is what determines how the inputs/outputs are resolved
            var cpDescBuilder = new CustomizationPointDescriptor.Builder(container, surfaceCP);
            cpDescBuilder.BlockDescriptors.Add(globalsProviderBlockDesc);
            cpDescBuilder.BlockDescriptors.Add(uvScrollBlockDesc);
            cpDescBuilder.BlockDescriptors.Add(albedoColorBlockDesc);

            surfaceCPDesc = cpDescBuilder.Build();
        }

        internal static Block BuildGlobalsProviderBlock(ShaderContainer container)
        {
            // A sample block that redirects some global variables into available inputs
            const string BlockName = "GlobalsProvider";

            var inputVariables = new List<BlockVariable>();
            var outputVariables = new List<BlockVariable>();

            // Make an output for 'TimeParameters'
            var timeParamsOutputBuilder = new BlockVariable.Builder(container);
            timeParamsOutputBuilder.Type = container._float4;
            timeParamsOutputBuilder.ReferenceName = "TimeParameters";
            var timeParamsOutput = timeParamsOutputBuilder.Build();
            outputVariables.Add(timeParamsOutput);

            var inputType = SimpleSampleBuilder.BuildStructFromVariables(container, $"{BlockName}Input", inputVariables);
            var outputType = SimpleSampleBuilder.BuildStructFromVariables(container, $"{BlockName}Output", outputVariables);

            // Simple copy the global '_TimeParameters' into the outputs
            var entryPointFnBuilder = new ShaderFunction.Builder(container, $"{BlockName}Main", outputType);
            entryPointFnBuilder.AddInput(inputType, "inputs");
            entryPointFnBuilder.AddLine($"{outputType.Name} outputs;");
            entryPointFnBuilder.AddLine($"outputs.{timeParamsOutput.ReferenceName} = _TimeParameters;");
            entryPointFnBuilder.AddLine($"return outputs;");
            var entryPointFn = entryPointFnBuilder.Build();

            // Setup the block from the inputs, outputs, types, functions
            var blockBuilder = new Block.Builder(container, BlockName);
            foreach (var variable in inputVariables)
                blockBuilder.AddInput(variable);
            foreach (var variable in outputVariables)
                blockBuilder.AddOutput(variable);
            blockBuilder.AddType(inputType);
            blockBuilder.AddType(outputType);
            blockBuilder.SetEntryPointFunction(entryPointFn);
            return blockBuilder.Build();
        }

        internal static Block BuildUvScrollBlock(ShaderContainer container)
        {
            // Make a sample block that takes in uv and scrolls it by time and a property
            const string BlockName = "UvScroll";

            var inputVariables = new List<BlockVariable>();
            var outputVariables = new List<BlockVariable>();

            // Make the uv0 variable. We can use the same variable as the input and output.
            var uvBuilder = new BlockVariable.Builder(container);
            uvBuilder.Type = container._float4;
            uvBuilder.ReferenceName = "uv0";
            var uv0 = uvBuilder.Build();
            inputVariables.Add(uv0);
            outputVariables.Add(uv0);

            // Take in 'TimeParameters' as a variable
            var timeParametersBuilder = new BlockVariable.Builder(container);
            timeParametersBuilder.Type = container._float4;
            timeParametersBuilder.ReferenceName = "TimeParameters";
            var timeParameters = timeParametersBuilder.Build();
            inputVariables.Add(timeParameters);

            // Make an input for the scroll speed to use. This input will also be a property.
            // For convenience, an input can be tagged with the [Property] attribute which will automatically add it as a property.
            var scrollSpeedBuilder = new BlockVariable.Builder(container);
            scrollSpeedBuilder.Type = container._float2;
            scrollSpeedBuilder.ReferenceName = "_ScrollSpeed";
            scrollSpeedBuilder.DisplayName = "ScrollSpeed";
            // Setup the material property info. We need to mark the default expression, the property type, and that it is a property.
            scrollSpeedBuilder.DefaultExpression = "(1, 1, 0, 0)";
            SimpleSampleBuilder.MarkAsProperty(container, scrollSpeedBuilder, "Vector");
            var scrollSpeed = scrollSpeedBuilder.Build();
            inputVariables.Add(scrollSpeed);

            var inputType = SimpleSampleBuilder.BuildStructFromVariables(container, $"{BlockName}Input", inputVariables);
            var outputType = SimpleSampleBuilder.BuildStructFromVariables(container, $"{BlockName}Output", outputVariables);

            // Build a function that takes in uv0, scales it by time and a speed, and then outputs it.
            var entryPointFnBuilder = new ShaderFunction.Builder(container, $"{BlockName}Main", outputType);
            entryPointFnBuilder.AddInput(inputType, "inputs");
            entryPointFnBuilder.AddLine($"{outputType.Name} outputs;");
            entryPointFnBuilder.AddLine($"float4 uv0 = inputs.{uv0.ReferenceName};");
            entryPointFnBuilder.AddLine($"uv0.xy += inputs.{scrollSpeed.ReferenceName} * inputs.{timeParameters.ReferenceName}[0];");
            entryPointFnBuilder.AddLine($"outputs.{uv0.ReferenceName} = uv0;");
            entryPointFnBuilder.AddLine($"return outputs;");
            var entryPointFn = entryPointFnBuilder.Build();

            // Setup the block from the inputs, outputs, types, functions
            var blockBuilder = new Block.Builder(container, BlockName);
            foreach (var variable in inputVariables)
                blockBuilder.AddInput(variable);
            foreach (var variable in outputVariables)
                blockBuilder.AddOutput(variable);
            blockBuilder.AddType(inputType);
            blockBuilder.AddType(outputType);
            blockBuilder.SetEntryPointFunction(entryPointFn);
            return blockBuilder.Build();
        }

        internal static Block BuildAlbedoColorBlock(ShaderContainer container)
        {
            const string BlockName = "AlbedoColor";

            var inputVariables = new List<BlockVariable>();
            var outputVariables = new List<BlockVariable>();
            var propertyVariables = new List<BlockVariable>();

            // Take in uv as an input
            var uv0InputBuilder = new BlockVariable.Builder(container);
            uv0InputBuilder.ReferenceName = "uv0";
            uv0InputBuilder.Type = container._float4;
            var uv0Input = uv0InputBuilder.Build();
            inputVariables.Add(uv0Input);

            // Make an input for Color. This input will also be a property.
            // For convenience, an input can be tagged with the [Property] attribute which will automatically add it as a property.
            var colorInputBuilder = new BlockVariable.Builder(container);
            colorInputBuilder.ReferenceName = "_Color";
            colorInputBuilder.DisplayName = "Color";
            colorInputBuilder.Type = container._float4;
            // Setup the material property info. We need to mark the default expression, the property type, and that it is a property.
            SimpleSampleBuilder.MarkAsProperty(container, colorInputBuilder, "Color");
            colorInputBuilder.DefaultExpression = "(1, 0, 0, 1)";
            var colorInput = colorInputBuilder.Build();
            inputVariables.Add(colorInput);

            // Make a texture for albedo color. Creating a texture is complicated so it's delegated to a helper.
            string albedoTexRefName = "_AlbedoTex";
            SimpleSampleBuilder.BuildTexture2D(container, albedoTexRefName, "AlbedoTex", inputVariables, propertyVariables);

            // Create an output for a float3 BaseColor.
            var colorOutBuilder = new BlockVariable.Builder(container);
            colorOutBuilder.ReferenceName = "BaseColor";
            colorOutBuilder.Type = container._float3;
            var colorOut = colorOutBuilder.Build();
            outputVariables.Add(colorOut);

            var inputType = SimpleSampleBuilder.BuildStructFromVariables(container, $"{BlockName}Input", inputVariables);
            var outputType = SimpleSampleBuilder.BuildStructFromVariables(container, $"{BlockName}Output", outputVariables);

            // Build the entry point function that samples the texture with the given uv and combines that with the color property.
            var entryPointFnBuilder = new ShaderFunction.Builder(container, "SurfaceFn", outputType);
            entryPointFnBuilder.AddInput(inputType, "inputs");
            entryPointFnBuilder.AddLine($"{outputType.Name} outputs;");
            entryPointFnBuilder.AddLine($"UnityTexture2D {albedoTexRefName}Tex = UnityBuildTexture2DStruct({albedoTexRefName});");
            entryPointFnBuilder.AddLine($"float4 {albedoTexRefName}Sample = SAMPLE_TEXTURE2D({albedoTexRefName}Tex.tex, {albedoTexRefName}Tex.samplerstate, {albedoTexRefName}Tex.GetTransformedUV(inputs.{uv0Input.ReferenceName}));");
            entryPointFnBuilder.AddLine($"outputs.{colorOut.ReferenceName} = inputs.{colorInput.ReferenceName} * {albedoTexRefName}Sample.xyz;");
            entryPointFnBuilder.AddLine($"return outputs;");
            var entryPointFn = entryPointFnBuilder.Build();

            // Setup the block from the inputs, outputs, types, functions
            var blockBuilder = new Block.Builder(container, BlockName);
            foreach (var variable in inputVariables)
                blockBuilder.AddInput(variable);
            foreach (var variable in outputVariables)
                blockBuilder.AddOutput(variable);
            foreach (var variable in propertyVariables)
                blockBuilder.AddProperty(variable);
            blockBuilder.AddType(inputType);
            blockBuilder.AddType(outputType);
            blockBuilder.SetEntryPointFunction(entryPointFn);
            return blockBuilder.Build();
        }

        static void DisplayTestResult(string testName, string code)
        {
            string tempPath = string.Format($"Temp/FoundryTest_{testName}.shader");
            if (ShaderGraph.GraphUtil.WriteToFile(tempPath, code))
                ShaderGraph.GraphUtil.OpenFile(tempPath);
        }
    }
}
