using UnityEngine;

public class ScriptableObjectUtility : MonoBehaviour
{
    public static T Clone<T>(T source) where T : ScriptableObject
    {
        return Object.Instantiate(source);
    }
}
