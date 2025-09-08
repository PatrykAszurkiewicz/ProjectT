using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WaveConfig))]
public class WaveConfigEditor : Editor
{
    SerializedProperty timeBetweenWavesProp;
    SerializedProperty wavesProp;

    void OnEnable()
    {
        timeBetweenWavesProp = serializedObject.FindProperty("timeBetweenWaves");
        wavesProp = serializedObject.FindProperty("waves");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(timeBetweenWavesProp);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Waves", EditorStyles.boldLabel);

        for (int i = 0; i < wavesProp.arraySize; i++)
        {
            SerializedProperty waveProp = wavesProp.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginVertical("box");

            // Wave number tylko do podgl¹du
            EditorGUILayout.LabelField("Wave Number", i.ToString());

            // Lista kierunków
            EditorGUILayout.PropertyField(waveProp.FindPropertyRelative("spawnDirections"), new GUIContent("Spawn Directions"), true);

            // Checkbox: wszyscy z jednego kierunku
            EditorGUILayout.PropertyField(waveProp.FindPropertyRelative("oneDirectionForAllEnemies"), new GUIContent("One Direction For All Enemies"));
            // Delaye
            EditorGUILayout.PropertyField(waveProp.FindPropertyRelative("minSpawnDelay"), new GUIContent("Min Spawn Delay"));
            EditorGUILayout.PropertyField(waveProp.FindPropertyRelative("maxSpawnDelay"), new GUIContent("Max Spawn Delay"));
            EditorGUILayout.PropertyField(waveProp.FindPropertyRelative("extraDelayBeforeStart"), new GUIContent("Extra Delay Before Start"));

            // Enemy groups
            SerializedProperty enemiesProp = waveProp.FindPropertyRelative("enemies");
            EditorGUILayout.LabelField("Enemies", EditorStyles.boldLabel);

            for (int j = 0; j < enemiesProp.arraySize; j++)
            {
                SerializedProperty enemyProp = enemiesProp.GetArrayElementAtIndex(j);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(enemyProp.FindPropertyRelative("enemyPrefab"), GUIContent.none);
                EditorGUILayout.PropertyField(enemyProp.FindPropertyRelative("count"), GUIContent.none, GUILayout.Width(60));

                if (GUILayout.Button("X", GUILayout.Width(20)))
                {
                    enemiesProp.DeleteArrayElementAtIndex(j);
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Enemy"))
            {
                enemiesProp.InsertArrayElementAtIndex(enemiesProp.arraySize);
            }

            EditorGUILayout.Space();

            // Remove wave button
            if (GUILayout.Button("Remove Wave"))
            {
                wavesProp.DeleteArrayElementAtIndex(i);
            }

            // Test Wave button (tylko w PlayMode)
            if (Application.isPlaying && GUILayout.Button("Test This Wave"))
            {
                WaveConfig config = (WaveConfig)target;
                FindAnyObjectByType<WaveSpawner>()?.TestWave(i);
            }

            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("Add Wave"))
        {
            wavesProp.InsertArrayElementAtIndex(wavesProp.arraySize);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
