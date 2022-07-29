using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.GraphToolsFoundation.Overdrive;
using UnityEditor.ShaderGraph.Generation;
using UnityEditor.ShaderGraph.GraphDelta;
using UnityEngine.GraphToolsFoundation.Overdrive;
using UnityEngine;

namespace UnityEditor.ShaderGraph.GraphUI
{
    using PreviewRenderMode = HeadlessPreviewManager.PreviewRenderMode;

    /// <summary>
    /// Manager class for node and master previews
    /// Layer that handles the interaction between GTF/Editor and the HeadlessPreviewManager
    /// </summary>
    public class PreviewManager
    {
        bool m_IsInitialized;

        public bool IsInitialized
        {
            get => m_IsInitialized;
            set => m_IsInitialized = value;
        }

        // Gets set to true when user selects the "Sprite" preview mesh in main preview
        bool m_LockMainPreviewRotation;

        public bool LockMainPreviewRotation
        {
            set => m_LockMainPreviewRotation = value;
        }

        HeadlessPreviewManager m_PreviewHandlerInstance;

        GraphModelStateComponent m_GraphModelStateComponent;

        HashSet<string> m_DirtyNodes;

        ShaderGraphModel m_GraphModel;

        Dictionary<string, SerializableGUID> m_NodeLookupTable;

        MainPreviewView m_MainPreviewView;
        MainPreviewData m_MainPreviewData;

        string m_MainContextNodeName = new Defs.ShaderGraphContext().GetRegistryKey().Name;

        int PreviewWidth => Mathf.FloorToInt(m_MainPreviewView.PreviewSize.x);
        int PreviewHeight => Mathf.FloorToInt(m_MainPreviewView.PreviewSize.y);

        internal PreviewManager(GraphModelStateComponent graphModelStateComponent)
        {
            m_GraphModelStateComponent = graphModelStateComponent;

            m_PreviewHandlerInstance = new HeadlessPreviewManager();

            m_DirtyNodes = new HashSet<string>();
            m_NodeLookupTable = new Dictionary<string, SerializableGUID>();
        }

        internal void Initialize(ShaderGraphModel graphModel, MainPreviewView mainPreviewView, bool wasWindowCloseCancelled)
        {
            // Can be null when the editor window is opened to the onboarding page
            if (graphModel == null)
                return;

            m_IsInitialized = true;
            m_GraphModel = graphModel;

            // Initialize the main preview
            m_MainPreviewView = mainPreviewView;
            m_MainPreviewData = graphModel.MainPreviewData;

            // Initialize the headless preview
            m_PreviewHandlerInstance.Initialize(m_MainContextNodeName, m_MainPreviewView.PreviewSize);

            m_PreviewHandlerInstance.SetActiveGraph(m_GraphModel.GraphHandler);
            m_PreviewHandlerInstance.SetActiveRegistry(m_GraphModel.RegistryInstance.Registry);
            m_PreviewHandlerInstance.SetActiveTarget(m_GraphModel.Targets.FirstOrDefault());

            // Initialize preview data for any nodes that exist on graph load
            foreach (var nodeModel in m_GraphModel.NodeModels)
            {
                if(nodeModel is GraphDataContextNodeModel contextNode && IsMainContextNode(nodeModel))
                    OnNodeAdded(contextNode.graphDataName, contextNode.Guid);
                else if (nodeModel is GraphDataNodeModel graphDataNodeModel && graphDataNodeModel.HasPreview)
                    OnNodeAdded(graphDataNodeModel.graphDataName, graphDataNodeModel.Guid);
            }

            // Call update once at graph load in order to handle updating all existing nodes
            Update();

            // Don't clear dirty state if the window close was cancelled with the graph in dirty state
            if (!wasWindowCloseCancelled)
            {
                // Mark dirty state as cleared afterwards to clear modification state from editor window tab
                m_GraphModel.Asset.Dirty = false;
            }
        }

        static bool IsMainContextNode(IGraphElementModel nodeModel)
        {
            return nodeModel is GraphDataContextNodeModel contextNode && contextNode.graphDataName == new Defs.ShaderGraphContext().GetRegistryKey().Name;
        }

        public void Update()
        {
            var updatedNodes = new List<string>();

            foreach (string nodeName in m_DirtyNodes)
            {
                if (!m_NodeLookupTable.TryGetValue(nodeName, out var nodeGuid))
                    continue;

                m_GraphModel.TryGetModelFromGuid(nodeGuid, out var nodeModel);
                if (IsMainContextNode(nodeModel))
                {
                    var previewOutputState = m_PreviewHandlerInstance.RequestMainPreviewTexture(
                        PreviewWidth,
                        PreviewHeight,
                        m_MainPreviewData.mesh,
                        m_MainPreviewData.scale,
                        m_LockMainPreviewRotation,
                        m_MainPreviewData.rotation,
                        out var updatedTexture,
                        out var shaderMessages);
                    if (updatedTexture != m_MainPreviewView.mainPreviewTexture)
                    {
                        // Headless preview manager handles assigning correct texture in case of completion, error state, still updating etc.
                        // We just need to update the texture on this side regardless of what happens
                        m_MainPreviewView.mainPreviewTexture = updatedTexture;

                        // Node is updated, remove from dirty list
                        if (previewOutputState == HeadlessPreviewManager.PreviewOutputState.Complete)
                            updatedNodes.Add(nodeName);
                        if (previewOutputState == HeadlessPreviewManager.PreviewOutputState.ShaderError)
                        {
                            updatedNodes.Add(nodeName);
                            // TODO: Handle displaying main preview errors
                            foreach (var errorMessage in shaderMessages)
                            {
                                Debug.Log(errorMessage);
                            }
                        }
                    }
                }
                else if (nodeModel is GraphDataNodeModel graphDataNodeModel && graphDataNodeModel.IsPreviewExpanded)
                {
                    var previewOutputState = m_PreviewHandlerInstance.RequestNodePreviewTexture(nodeName, out var nodeRenderOutput, out var shaderMessages);
                    if (nodeRenderOutput != graphDataNodeModel.PreviewTexture)
                    {
                        // Headless preview manager handles assigning correct texture in case of completion, error state, still updating etc.
                        // We just need to update the texture on this side regardless of what happens
                        graphDataNodeModel.OnPreviewTextureUpdated(nodeRenderOutput);

                        using var graphUpdater = m_GraphModelStateComponent.UpdateScope;
                        {
                            graphUpdater.MarkChanged(nodeModel);
                        }

                        // Node is updated, remove from dirty list
                        if (previewOutputState == HeadlessPreviewManager.PreviewOutputState.Complete)
                            updatedNodes.Add(nodeName);
                        if (previewOutputState == HeadlessPreviewManager.PreviewOutputState.ShaderError)
                        {
                            updatedNodes.Add(nodeName);
                            // TODO: Handle displaying error badges on nodes
                            foreach (var errorMessage in shaderMessages)
                            {
                                Debug.Log(errorMessage);
                            }
                        }
                    }
                }
            }

            // Clean up any nodes that were successfully updated from the dirty list
            foreach (var updatedNode in updatedNodes)
                m_DirtyNodes.Remove(updatedNode);

        }

        public Texture GetCachedMainPreviewTexture()
        {
            m_PreviewHandlerInstance.RequestMainPreviewTexture(
                PreviewWidth,
                PreviewHeight,
                m_MainPreviewData.mesh,
                m_MainPreviewData.scale,
                m_LockMainPreviewRotation,
                m_MainPreviewData.rotation,
                out var cachedMainPreviewTexture,
                out var shaderMessages);

            return cachedMainPreviewTexture;
        }

        // When active target is changed, we need to re-generate and re-render everything
        internal void OnActiveTargetChanged(Target newActiveTarget)
        {
            m_PreviewHandlerInstance.SetActiveTarget(newActiveTarget);
            OnTargetSettingsChanged();
        }

        // When target settings are changed, we need to re-generate and re-render everything
        internal void OnTargetSettingsChanged()
        {
            foreach (var (nodeName, nodeGuid) in m_NodeLookupTable)
                OnNodeFlowChanged(nodeName);
        }

        /// <summary>
        /// This can be called when the main preview's mesh, zoom, rotation etc. changes and a re-render is required
        /// </summary>
        public void OnMainPreviewDataChanged()
        {
            m_DirtyNodes.Add(m_MainContextNodeName);
        }

        // TODO: Implement changing preview mode
        public void OnPreviewModeChanged(string nodeName, PreviewRenderMode newPreviewMode) { }


        /// <summary>
        /// Used to notify when a node's connections have been changed
        /// </summary>
        /// <param name="nodeName"> Name of node whose connections were modified </param>
        /// <param name="wasNodeDeleted"> Flag to set to true if this node was deleted in this modification </param>
        public void OnNodeFlowChanged(string nodeName, bool wasNodeDeleted = false)
        {
            if (wasNodeDeleted)
            {
                OnNodeRemoved(nodeName);
                m_PreviewHandlerInstance.NotifyNodeFlowChanged(nodeName, true);
            }
            else
            {
                m_DirtyNodes.Add(nodeName);
                var impactedNodes = m_PreviewHandlerInstance.NotifyNodeFlowChanged(nodeName);
                foreach (var downstreamNode in impactedNodes)
                {
                    m_DirtyNodes.Add(downstreamNode);
                }
            }
        }

        public void OnNodeAdded(String nodeName, SerializableGUID nodeGuid)
        {
            if (m_NodeLookupTable.ContainsKey(nodeName))
                return;

            // Add node to dirty list so preview image can be updated next frame
            m_DirtyNodes.Add(nodeName);

            m_NodeLookupTable.Add(nodeName, nodeGuid);
        }

        void OnNodeRemoved(String nodeName)
        {
            m_DirtyNodes.Remove(nodeName);
            m_NodeLookupTable.Remove(nodeName);
        }

        public void OnGlobalPropertyChanged(string propertyName, object newValue)
        {
            var linkedVariableNodes =  m_GraphModel.GetLinkedVariableNodes(propertyName);

            var variableNodeNames = new List<string>();
            foreach(var node in linkedVariableNodes)
            {
                var nodeModel = node as GraphDataVariableNodeModel;
                variableNodeNames.Add(nodeModel.graphDataName);
            }

            var impactedNodes = m_PreviewHandlerInstance.SetGlobalProperty(propertyName, newValue, variableNodeNames);
            foreach (var downstreamNode in impactedNodes)
            {
                m_DirtyNodes.Add(downstreamNode);
            }
        }

        public void OnLocalPropertyChanged(string nodeName, string propertyName, object newValue)
        {
            m_DirtyNodes.Add(nodeName);
            var impactedNodes = m_PreviewHandlerInstance.SetLocalProperty(nodeName, propertyName, newValue);
            foreach (var downstreamNode in impactedNodes)
            {
                m_DirtyNodes.Add(downstreamNode);
            }
        }

        /// <summary>
        /// Used by UI tests to validate preview results after UI driven changes
        /// </summary>
        /// <param name="nodeName"></param>
        internal Material GetPreviewMaterialForNode(string nodeName)
        {
            return m_PreviewHandlerInstance.RequestNodePreviewMaterial(nodeName);
        }
    }
}
