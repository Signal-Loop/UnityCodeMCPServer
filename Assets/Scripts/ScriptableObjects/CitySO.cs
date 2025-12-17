using UnityEngine;

[System.Serializable]
public class CitySO : ScriptableObject
{
    public string CityName;
    public Vector2 Coordinates;
    [TextArea]
    public string Description;
}
