using UnityEngine;
using UnityEditor;
using SwiftCollections.Dimensions;

namespace Lockstep.Environment.Editors
{
    [CustomEditor(typeof(HeightMapSaver))]
    public class EditorHeightmapSaver : Editor
    {
        SerializedProperty Size;
        SerializedProperty BottomLeft;
        SerializedProperty HeightBounds;
        SerializedProperty Interval;

        public override void OnInspectorGUI()
        {
            HeightMapSaver hh = (HeightMapSaver)target;

            SerializedObject so = new SerializedObject(hh);
            GenerateProperties(so);

            Size.Draw();
            BottomLeft.Draw();
            HeightBounds.Draw();
            Interval.Draw();

            so.FindProperty("_bonusHeight").Draw();
            so.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Maps", EditorStyles.boldLabel);
            SerializedProperty Maps = so.FindProperty("_maps");

            int size = EditorGUILayout.IntField("Map Count", Maps.arraySize);
            if (size != Maps.arraySize)
                Maps.arraySize = size;

            so.ApplyModifiedProperties();

            for (int i = 0; i < hh.Maps.Length; i++)
            {
                HeightMap hm = hh.Maps[i];
                SerializedProperty heightMapProp = Maps.GetArrayElementAtIndex(i);
                EditorGUILayout.PropertyField(heightMapProp, new GUIContent("Map " + hm.Name), true);
                so.ApplyModifiedProperties();


                if (GUILayout.Button("Scan"))
                {
                    short[,] Scan = hh.Scan(hh.Maps[i].ScanLayers.value);
                    hm.Map = new ShortArray2D(Scan);
                }

                EditorGUILayout.Space();
            }

            SerializedProperty showProp = so.FindProperty("_show");
            showProp.Draw();

            EditorGUILayout.Space();

            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(hh);
        }

        private void GenerateProperties(SerializedObject so)
        {
            Size = so.FindProperty("_size");
            BottomLeft = so.FindProperty("_bottomLeft");
            Interval = so.FindProperty("_interval");
            HeightBounds = so.FindProperty("_heightBounds");
        }
    }
}