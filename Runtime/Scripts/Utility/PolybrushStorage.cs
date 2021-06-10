namespace UnityEngine.Polybrush
{
    // (ASG): This class is used to story the polybrush mesh data in a separate asset file, to avoid cluttering scene and prefab files.
    public class PolybrushStorage : ScriptableObject
    {
        // The actual polybrush data.
        [SerializeField]
        internal PolyMesh PolyMesh;
    }
}
