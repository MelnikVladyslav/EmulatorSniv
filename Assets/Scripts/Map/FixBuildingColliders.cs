#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class FixBuildingColliders : EditorWindow
{
    [MenuItem("Tools/Fix Building Colliders")]
    public static void ShowWindow()
    {
        if (!EditorUtility.DisplayDialog(
            "Fix Building Colliders",
            "Цей скрипт додасть MeshCollider до всіх мешів у вибраних об'єктах (або у всій сцені, якщо нічого не вибрано). Продовжити?",
            "Так", "Скасувати"))
            return;

        int fixedCount = 0;
        int skippedCount = 0;

        GameObject[] roots;

        if (Selection.gameObjects.Length > 0)
        {
            roots = Selection.gameObjects;
        }
        else
        {
            roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        }

        foreach (GameObject root in roots)
        {
            MeshFilter[] meshes = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter mf in meshes)
            {
                GameObject go = mf.gameObject;
                MeshCollider mc = go.GetComponent<MeshCollider>();

                if (mf.sharedMesh == null)
                {
                    skippedCount++;
                    continue;
                }

                if (mc == null)
                {
                    mc = go.AddComponent<MeshCollider>();
                    fixedCount++;
                }

                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                mc.isTrigger = false;
            }
        }

        EditorUtility.DisplayDialog(
            "Готово ✅",
            $"Виправлено колайдерів: {fixedCount}\nПропущено мешів: {skippedCount}",
            "OK"
        );
    }
}
#endif