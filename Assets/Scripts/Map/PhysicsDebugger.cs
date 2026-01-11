using UnityEngine;

#if UNITY_EDITOR
[ExecuteInEditMode]
#endif
public class PhysicsDebugger : MonoBehaviour
{
    [Header("Debug Settings")]
    public float detectionRadius = 50f;
    public bool showOnlyBuildings = false;
    public LayerMask layerMask = ~0; // за замовчуванням показує всі шари

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Collider[] cols = Physics.OverlapSphere(transform.position, detectionRadius, layerMask);

        foreach (var col in cols)
        {
            if (showOnlyBuildings && !col.name.ToLower().Contains("building"))
                continue;

            // Отримуємо колір за типом колайдера
            Color gizmoColor = Color.green;
            if (col.isTrigger) gizmoColor = Color.red;
            else if (col is MeshCollider mc && mc.convex) gizmoColor = Color.yellow;

            Gizmos.color = gizmoColor;

            Bounds b = col.bounds;
            Gizmos.DrawWireCube(b.center, b.size);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(b.center + Vector3.up * 1.5f, col.name);
#endif
        }
    }
}