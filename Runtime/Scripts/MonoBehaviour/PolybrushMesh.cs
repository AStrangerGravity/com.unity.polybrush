using System;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;

#endif

// (ASG): TODO: These changes don't function properly with the "Override Mesh" option.
//          When hovering, then clicking (painting) and the un-hovering, the Polybrush-*** mesh is deleted. This happens
//          during the normal EditableObject.Revert() graphicsmesh call (this delete is important, because it is used for IsValid).
//          But I think the graphicsmesh should be set to a *new* mesh after painting, to reflect the new hover state.
//          This doesn't seem to be happening.
//          This means we can't use this system with skeletal meshes.
//          A new EditableObject is created for every change (see "GetOrCreateBrushTarget"). One is created as well for hovers.

namespace UnityEngine.Polybrush
{
    /// <summary>
    /// Script that will be attached at all time on any polybrush object
    /// It will be used to store a copy of the mesh inside a polymesh object (that is serializable).
    /// It will also handle the advs switch mode (on/off)
    /// </summary>
    [ExecuteInEditMode]
    class PolybrushMesh : MonoBehaviour
    {
        internal enum Mode
        {
            Mesh,
            AdditionalVertexStream
        }

        internal static class Styles
        {
            internal const string k_VertexMismatchStringFormat =
                "Warning! The GameObject \"{0}\" cannot apply it's 'Additional Vertex Streams' mesh, because it's base mesh has changed and has a different vertex count.";
        }

        internal enum ObjectType
        {
            Mesh,
            SkinnedMesh
        }

        /// <summary>
        /// References components needed by Polybrush: MeshFilter, MeshRenderer and SkinnedMeshRenderer.
        /// Will cache references if they are found to avoid additional GetComponent() calls.
        /// </summary>
        internal struct MeshComponentsCache
        {
            GameObject m_Owner;
            MeshFilter m_MeshFilter;
            MeshRenderer m_MeshRenderer;
            SkinnedMeshRenderer m_SkinMeshRenderer;

            internal bool IsValid()
            {
                return m_Owner != null;
            }

            internal MeshFilter MeshFilter
            {
                get { return m_MeshFilter; }
            }

            internal MeshRenderer MeshRenderer
            {
                get { return m_MeshRenderer; }
            }

            internal SkinnedMeshRenderer SkinnedMeshRenderer
            {
                get { return m_SkinMeshRenderer; }
            }

            internal MeshComponentsCache(GameObject root)
            {
                m_Owner = root;
                m_MeshFilter = root.GetComponent<MeshFilter>();
                m_MeshRenderer = root.GetComponent<MeshRenderer>();
                m_SkinMeshRenderer = root.GetComponent<SkinnedMeshRenderer>();
            }
        }

        // (ASG) Changed to storage, so it stores this data on an asset.
        //seriazlied polymesh stored on the component
        [SerializeField]
        PolybrushStorage m_PolyMesh;

        //mesh ref from the skinmesh asset, if any
        [SerializeField]
        Mesh m_SkinMeshRef;

        [SerializeField]
        Mesh m_OriginalMeshObject;

        [SerializeField]
        Mode m_Mode;

        MeshComponentsCache m_ComponentsCache;

        /// <summary>
        /// Accessor to Unity rendering related components attached to the same GameObject as this component.
        /// </summary>
        internal MeshComponentsCache componentsCache
        {
            get { return m_ComponentsCache; }
        }

        // (ASG)
        internal PolybrushStorage storage
        {
            get { return m_PolyMesh; }
        }

        /// <summary>
        /// Returns true if there are applied modification on this mesh.
        /// </summary>
        internal bool hasAppliedChanges
        {
            get
            {
                if (m_ComponentsCache.MeshFilter)
                    return m_PolyMesh.PolyMesh.ToUnityMesh() != m_ComponentsCache.MeshFilter.sharedMesh;
                if (m_ComponentsCache.SkinnedMeshRenderer)
                    return m_PolyMesh.PolyMesh.ToUnityMesh() != m_ComponentsCache.SkinnedMeshRenderer.sharedMesh;
                return false;
            }
        }

        internal bool hasAppliedAdditionalVertexStreams
        {
            get
            {
                if (m_ComponentsCache.MeshRenderer != null &&
                    m_ComponentsCache.MeshRenderer.additionalVertexStreams != null)
                    return m_ComponentsCache.MeshRenderer.additionalVertexStreams == m_PolyMesh.PolyMesh.ToUnityMesh();
                return false;
            }
        }

        //access to the polymesh directly
        internal PolyMesh polyMesh
        {
            get
            {
                if (m_PolyMesh == null)
                {
                    return null;
                }

                return m_PolyMesh.PolyMesh;
            }
        }

        //get: will convert the data stored inside polymesh to a mesh, return it
        internal Mesh storedMesh
        {
            get
            {
                if (m_PolyMesh != null)
                {
                    return m_PolyMesh.PolyMesh.ToUnityMesh();
                }
                else
                {
                    return null;
                }
            }
        }

        internal ObjectType type
        {
            get
            {
                if (m_ComponentsCache.SkinnedMeshRenderer)
                    return ObjectType.SkinnedMesh;
                return ObjectType.Mesh;
            }
        }

        [Obsolete()]
        internal static bool s_UseADVS { private get; set; }

        internal Mode mode
        {
            get { return m_Mode; }
            set { UpdateMode(value); }
        }

        internal Mesh skinMeshRef
        {
            get { return m_SkinMeshRef; }
            set { m_SkinMeshRef = value; }
        }

        internal Mesh sourceMesh
        {
            get { return m_OriginalMeshObject; }
        }

        bool m_Initialized = false;

        /// <summary>
        /// Returns true if internal data has been initialized.
        /// If returns false, use <see cref="Initialize"/>.
        /// </summary>
        internal bool isInitialized
        {
            get { return m_Initialized; }
        }

        /// <summary>
        /// Initializes non-serialized internal cache in the component.
        /// Should be called after instantiation.
        /// Use <see cref="isInitialized"/> to check if it has already been initialized.
        /// </summary>
        internal void Initialize()
        {
            if (isInitialized)
                return;

            if (!m_ComponentsCache.IsValid())
                m_ComponentsCache = new MeshComponentsCache(gameObject);

            Mesh mesh = null;

            if (m_ComponentsCache.MeshFilter != null)
            {
                mesh = m_ComponentsCache.MeshFilter.sharedMesh;
                if (m_OriginalMeshObject == null)
                    m_OriginalMeshObject = mesh;
            }
            else if (m_ComponentsCache.SkinnedMeshRenderer != null)
                mesh = m_ComponentsCache.SkinnedMeshRenderer.sharedMesh;

            EnsureStorageCreated();

            if (!polyMesh.IsValid() && mesh)
                SetMesh(mesh);
            else if (polyMesh.IsValid())
                SetMesh(polyMesh.ToUnityMesh());

            m_Initialized = true;
        }

        private void EnsureStorageCreated()
        {
            if (m_PolyMesh == null)
            {
#if UNITY_EDITOR
                string id = GlobalObjectId.GetGlobalObjectIdSlow(m_ComponentsCache.MeshFilter).ToString();
                string directory = "Assets/Plugins/PolybrushData/";
                string path = directory + id + ".asset";
                new DirectoryInfo(directory).Create(); // Ensure it's createed

                PolybrushStorage asset = AssetDatabase.LoadAssetAtPath<PolybrushStorage>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<PolybrushStorage>();
                    asset.PolyMesh = new PolyMesh();
                    AssetDatabase.CreateAsset(asset, path);
                }

                m_PolyMesh = asset;
#else
                // Create empty storage asset.
                PolybrushStorage asset = ScriptableObject.CreateInstance<PolybrushStorage>();
                asset.PolyMesh = new PolyMesh();
                m_PolyMesh = asset;
#endif
            }
        }

        /// <summary>
        /// Update Polymesh data with the given Unity Mesh information.
        /// </summary>
        /// <param name="unityMesh">Unity mesh.</param>
        internal void SetMesh(Mesh unityMesh)
        {
            if (unityMesh == null)
                return;

            #if UNITY_EDITOR
            // If the mesh is not within an asset, save it into the storage.
            if (!AssetDatabase.Contains(unityMesh))
            {
                EnsureStorageCreated();

                Debug.Log($"Extracting Polymesh [{unityMesh.name}] to Assets folder");

                var subassets = AssetDatabase.LoadAllAssetRepresentationsAtPath(AssetDatabase.GetAssetPath(storage));
                if (subassets != null)
                {
                    foreach (Object a in subassets)
                    {
                        if (a != null)
                        {
                            AssetDatabase.RemoveObjectFromAsset(a);
                        }
                    }
                }

                AssetDatabase.AddObjectToAsset(unityMesh, m_PolyMesh);
                AssetDatabase.SaveAssets();
            }
            #endif

            m_PolyMesh.PolyMesh.InitializeWithUnityMesh(unityMesh);
            SynchronizeWithMeshRenderer();
        }

        internal void SetAdditionalVertexStreams(Mesh vertexStreams)
        {
            m_PolyMesh.PolyMesh.ApplyAttributesFromUnityMesh(vertexStreams, MeshChannelUtility.ToMask(vertexStreams));
            SynchronizeWithMeshRenderer();
        }

        /// <summary>
        /// Update GameObject's renderers with PolybrushMesh data.
        /// </summary>
        internal void SynchronizeWithMeshRenderer()
        {
            if (m_PolyMesh == null)
                return;

            m_PolyMesh.PolyMesh.UpdateMeshFromData();

            if (m_ComponentsCache.SkinnedMeshRenderer != null && skinMeshRef != null)
                UpdateSkinMesh();

            if (mode == Mode.Mesh)
            {
                if (m_ComponentsCache.MeshFilter != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        Undo.RecordObject(m_ComponentsCache.MeshFilter, "Assign new mesh to MeshFilter");
#endif
                    m_ComponentsCache.MeshFilter.sharedMesh = m_PolyMesh.PolyMesh.ToUnityMesh();
                }

                SetAdditionalVertexStreamsOnRenderer(null);
            }
            else
            {
                if (!CanApplyAdditionalVertexStreams())
                {
                    if (hasAppliedAdditionalVertexStreams)
                        RemoveAdditionalVertexStreams();

                    Debug.LogWarning(string.Format(Styles.k_VertexMismatchStringFormat, gameObject.name), this);
                    return;
                }

                SetAdditionalVertexStreamsOnRenderer(m_PolyMesh.PolyMesh.ToUnityMesh());
            }
        }

        /// <summary>
        /// Checks if all conditions are met to apply Additional Vertex Streams.
        /// </summary>
        /// <returns></returns>
        internal bool CanApplyAdditionalVertexStreams()
        {
            // If "Use additional vertex streams is enabled in Preferences."
            if (mode == Mode.AdditionalVertexStream)
            {
                if (m_ComponentsCache.MeshFilter != null && m_ComponentsCache.MeshFilter.sharedMesh != null)
                {
                    if (m_ComponentsCache.MeshFilter.sharedMesh.vertexCount != polyMesh.vertexCount)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Takes the mesh from the skinmesh in the asset (saved by EditableObject during creation) and apply skinmesh information to the regular stored mesh.
        /// Then set it to the skinmeshrenderer.
        /// </summary>
        void UpdateSkinMesh()
        {
            Mesh mesh = skinMeshRef;

            Mesh generatedMesh = m_PolyMesh.PolyMesh.ToUnityMesh();
            generatedMesh.boneWeights = mesh.boneWeights;
            generatedMesh.bindposes = mesh.bindposes;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(m_ComponentsCache.SkinnedMeshRenderer, "Assign new mesh to SkinnedMeshRenderer");
#endif
            m_ComponentsCache.SkinnedMeshRenderer.sharedMesh = generatedMesh;
        }

        /// <summary>
        /// Set the Additional Vertex Streams on the current MeshRenderer.
        /// </summary>
        /// <param name="mesh"></param>
        void SetAdditionalVertexStreamsOnRenderer(Mesh mesh)
        {
            if (m_ComponentsCache.MeshRenderer != null)
                m_ComponentsCache.MeshRenderer.additionalVertexStreams = mesh;
        }

        internal void RemoveAdditionalVertexStreams()
        {
            SetAdditionalVertexStreamsOnRenderer(null);
        }

        void UpdateMode(Mode newMode)
        {
            // SkinnedMesh can only work in baked mode.
            // It doesn't support Additional Vertex Streams.
            if (type == ObjectType.SkinnedMesh && m_Mode != Mode.Mesh)
            {
                m_Mode = Mode.Mesh;
                return;
            }

            m_Mode = newMode;
            if (mode == Mode.AdditionalVertexStream)
            {
                if (m_ComponentsCache.MeshFilter != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        Undo.RecordObject(m_ComponentsCache.MeshFilter, "Assign new mesh to MeshFilter");
#endif
                    m_ComponentsCache.MeshFilter.sharedMesh = m_OriginalMeshObject;
                }

                SetMesh(m_PolyMesh.PolyMesh.ToUnityMesh());
            }
            else if (mode == Mode.Mesh)
            {
                SetMesh(m_PolyMesh.PolyMesh.ToUnityMesh());
            }
        }

        void OnEnable()
        {
            m_Initialized = false;

            if (!isInitialized)
                Initialize();
        }

        void OnDestroy()
        {
            // Time.frameCount is zero when loading scenes in the Editor. It's the only way I could figure to
            // differentiate between OnDestroy invoked from user delete & editor scene loading.
            if (Application.isEditor &&
                !Application.isPlaying &&
                Time.frameCount > 0)
            {
                // Re-assign source mesh only if it has changed.
                // Likely to happen with ProBuilder.
                if (type == ObjectType.Mesh && componentsCache.MeshFilter != null &&
                    sourceMesh == componentsCache.MeshFilter.sharedMesh)
                    return;

                SetMesh(sourceMesh);
            }
        }
    }
}
