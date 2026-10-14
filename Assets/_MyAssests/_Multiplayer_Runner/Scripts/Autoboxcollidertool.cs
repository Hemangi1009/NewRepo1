using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Attach to a parent object. Use the Inspector's 3-dot menu (or right-click the component header):
///   - "Add Box Colliders To Children"  -> adds a fitted BoxCollider to every child that has a mesh
///   - "Remove Added Box Colliders"     -> removes only the colliders this tool added
/// </summary>
public class AutoBoxColliderTool : MonoBehaviour
{
    [Tooltip("Also process inactive child objects.")]
    [SerializeField] private bool includeInactive = true;

    [Tooltip("Skip objects that already have any collider.")]
    [SerializeField] private bool skipIfColliderExists = true;

    [Tooltip("Also add a collider to this root object if it has a mesh.")]
    [SerializeField] private bool includeSelf = true;

    // Remembers which colliders were added by this tool so revert never touches your own colliders.
    [SerializeField, HideInInspector]
    private List<BoxCollider> addedColliders = new List<BoxCollider>();

    [ContextMenu("Add Box Colliders To Children")]
    public void AddBoxColliders()
    {
        int added = 0;
        var processed = new HashSet<GameObject>();

        // Static / normal meshes
        foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(includeInactive))
        {
            if (mf.sharedMesh == null) continue;
            if (TryAdd(mf.gameObject, mf.sharedMesh.bounds, processed)) added++;
        }

        // Skinned meshes (characters etc.)
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive))
        {
            if (smr.sharedMesh == null) continue;
            if (TryAdd(smr.gameObject, smr.sharedMesh.bounds, processed)) added++;
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        Debug.Log($"[AutoBoxColliderTool] Added {added} BoxCollider(s) under '{name}'.", this);
    }

    [ContextMenu("Remove Added Box Colliders")]
    public void RemoveAddedBoxColliders()
    {
        int removed = 0;

#if UNITY_EDITOR
        Undo.RecordObject(this, "Remove Box Colliders");
#endif
        foreach (BoxCollider col in addedColliders)
        {
            if (col == null) continue;
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.DestroyObjectImmediate(col);
            else Destroy(col);
#else
            Destroy(col);
#endif
            removed++;
        }
        addedColliders.Clear();

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        Debug.Log($"[AutoBoxColliderTool] Removed {removed} BoxCollider(s).", this);
    }

    private bool TryAdd(GameObject go, Bounds localMeshBounds, HashSet<GameObject> processed)
    {
        if (!includeSelf && go == gameObject) return false;
        if (!processed.Add(go)) return false;                       // already handled this object
        if (skipIfColliderExists && go.GetComponent<Collider>() != null) return false;

#if UNITY_EDITOR
        BoxCollider box;
        if (!Application.isPlaying)
        {
            Undo.RecordObject(this, "Add Box Colliders");
            box = Undo.AddComponent<BoxCollider>(go);
        }
        else box = go.AddComponent<BoxCollider>();
#else
        BoxCollider box = go.AddComponent<BoxCollider>();
#endif

        // Mesh bounds are in the object's local space, exactly what BoxCollider expects.
        box.center = localMeshBounds.center;
        box.size = localMeshBounds.size;

        addedColliders.Add(box);
        return true;
    }
}