using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class NamedMaterial
{
    public Material material;
    [Tooltip("Color name used in label (e.g. red, blue, green)")]
    public string colorName;
}

[CreateAssetMenu(fileName = "New MaterialSet", menuName = "PickPlace/Material Set")]
public class MaterialSet : ScriptableObject
{
    public List<NamedMaterial> materials;
}
