using UnityEngine;
using UnityEditor;
using static UnityEditor.Searcher.SearcherWindow.Alignment;
using static UnityEditor.Handles;
using UnityEngine.UIElements;
using System.Linq;

[CustomEditor(typeof(GaussianViewer))]
public class GaussianViewerEditor : Editor
{
    GaussianViewer gaussianViewer;

    SerializedProperty maxGaussianCount;
    SerializedProperty mesh;
    SerializedProperty material;

    private void OnEnable()
    {
        maxGaussianCount = serializedObject.FindProperty("maxGaussianCount");
        mesh = serializedObject.FindProperty("mesh");
        material = serializedObject.FindProperty("material");
    }

    public override void OnInspectorGUI()
    {
        gaussianViewer = (GaussianViewer)target;

        serializedObject.Update();

        // file path
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("PLY File Path", GUILayout.Width(100));
        Rect rect = EditorGUILayout.GetControlRect(GUILayout.Width(400));
        gaussianViewer.filePath = EditorGUI.TextField(rect, gaussianViewer.filePath);
        EditorGUILayout.EndHorizontal();
        // Drag and drop
        if ((Event.current.type == EventType.DragUpdated
            || Event.current.type == EventType.DragPerform)
            && rect.Contains(Event.current.mousePosition))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
            if (Event.current.type == EventType.DragPerform
                && DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
            {
                gaussianViewer.filePath = DragAndDrop.paths[0];
            }
        }

        // max gaussian count
        EditorGUILayout.PropertyField(maxGaussianCount);
        // mesh
        EditorGUILayout.PropertyField(mesh);
        // material
        EditorGUILayout.PropertyField(material);

        if (GUILayout.Button("Load Point Cloud"))
        {
            gaussianViewer.LoadPointCloud();
            gaussianViewer.PreprocessData();
        }

        if (GUILayout.Button("Apply Changes"))
        {
            gaussianViewer.PreprocessData();
        }

        GUILayout.Space(10);
        int loadGaussianCount = gaussianViewer.vertices != null ? gaussianViewer.vertices.Count : 0;
        GUILayout.TextArea("Total Gaussians : " + loadGaussianCount
            + "\n Current Rendering Gaussians : " + gaussianViewer.GetRenderingGaussianCount());

        serializedObject.ApplyModifiedProperties();
    }


    protected void OnSceneGUI()
    {
        if(gaussianViewer == null)
        {
            return;
        }
        // 父节点的世界空间坐标
        Vector3 originPos = gaussianViewer.transform.position;

        // 裁剪盒子的世界空间中心坐标
        Vector3 boxCentor = gaussianViewer.cullBox.Center() + originPos;

        Handles.color = Handles.xAxisColor;
        gaussianViewer.cullBox.xMin = Handles.FreeMoveHandle(
            new Vector3(gaussianViewer.cullBox.xMin + originPos.x, boxCentor.y, boxCentor.z),
            0.05f, Vector3.one, Handles.DotHandleCap).x - originPos.x;
        gaussianViewer.cullBox.xMax = Handles.FreeMoveHandle(
            new Vector3(gaussianViewer.cullBox.xMax + originPos.x, boxCentor.y, boxCentor.z),
            0.05f, Vector3.one, Handles.DotHandleCap).x - originPos.x;

        Handles.color = Handles.yAxisColor;
        gaussianViewer.cullBox.yMin = Handles.FreeMoveHandle(
            new Vector3(boxCentor.x, gaussianViewer.cullBox.yMin + originPos.y, boxCentor.z),
            0.05f, Vector3.one, Handles.DotHandleCap).y - originPos.y;
        gaussianViewer.cullBox.yMax = Handles.FreeMoveHandle(
            new Vector3(boxCentor.x, gaussianViewer.cullBox.yMax + originPos.y, boxCentor.z),
            0.05f, Vector3.one, Handles.DotHandleCap).y - originPos.y;

        Handles.color = Handles.zAxisColor;
        gaussianViewer.cullBox.zMin = Handles.FreeMoveHandle(
            new Vector3(boxCentor.x, boxCentor.y, gaussianViewer.cullBox.zMin + originPos.z),
            0.05f, Vector3.one, Handles.DotHandleCap).z - originPos.z;
        gaussianViewer.cullBox.zMax = Handles.FreeMoveHandle(
            new Vector3(boxCentor.x, boxCentor.y, gaussianViewer.cullBox.zMax + originPos.z),
            0.05f, Vector3.one, Handles.DotHandleCap).z - originPos.z;
    }


}

