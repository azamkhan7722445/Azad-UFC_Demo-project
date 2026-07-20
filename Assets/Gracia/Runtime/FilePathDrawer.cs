#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(GraciaSplatsFilePathAttribute))]
public class FilePathDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.LabelField(position, label.text, "Use FilePath with string.");
            return;
        }

        var filePathAttribute = attribute as GraciaSplatsFilePathAttribute;

        Rect textFieldPosition = position;
        textFieldPosition.width -= 30;

        Rect buttonPosition = position;
        buttonPosition.x += textFieldPosition.width;
        buttonPosition.width = 30;

        property.stringValue = EditorGUI.TextField(textFieldPosition, label, property.stringValue);

        if (GUI.Button(buttonPosition, "..."))
        {
            string path = EditorUtility.OpenFilePanel(filePathAttribute.Title, filePathAttribute.Directory,
                                                      filePathAttribute.Extension);
            if (!string.IsNullOrEmpty(path))
            {
                property.stringValue = path;
                property.serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
#endif