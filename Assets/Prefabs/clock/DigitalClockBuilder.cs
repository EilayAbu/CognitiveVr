using UnityEngine;
using UnityEngine.UI;
using TMPro; //

public class DigitalClockBuilder : MonoBehaviour //
{
    [Header("Settings")] //[cite: 1]
    [SerializeField] private Vector2 boardSize = new Vector2(300, 150); //[cite: 1]
    [SerializeField] private Color boardColor = Color.black; //[cite: 1]
    [SerializeField] private Color timerColor = Color.green; //[cite: 1]

    // הפונקציה הזו מוסיפה כפתור לתפריט של הקומפוננטה ביוניטי[cite: 1]
    [ContextMenu("Build Digital Clock Now")] //[cite: 1]
    public void BuildClock()
    {
        // 1. יצירת האובייקט הראשי (Canvas)[cite: 1]
        GameObject rootObj = new GameObject("Auto_DigitalClock"); //[cite: 1]
        Canvas canvas = rootObj.AddComponent<Canvas>(); //[cite: 1]
        canvas.renderMode = RenderMode.WorldSpace; //[cite: 1]

        // הגדרה לגודל שמתאים למרחב תלת-ממדי[cite: 1]
        rootObj.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f); //[cite: 1]
        rootObj.transform.position = new Vector3(0, 2, 5); // שם אותו מול המצלמה[cite: 1]

        // 2. יצירת הרקע[cite: 1]
        GameObject bgObj = new GameObject("Background"); //[cite: 1]
        bgObj.transform.SetParent(rootObj.transform, false); //[cite: 1]
        Image bgImage = bgObj.AddComponent<Image>(); //[cite: 1]
        bgImage.color = boardColor; //[cite: 1]
        bgImage.rectTransform.sizeDelta = boardSize; //[cite: 1]

        // 3. הוספת הסקריפט הלוגי שמעדכן את השעה
        DigitalClock controller = rootObj.AddComponent<DigitalClock>();

        // 4. יצירת הטקסט של השעון
        TextMeshProUGUI txtTimer = CreateText(rootObj, "Timer", "00:00", Vector2.zero, 80, timerColor); //[cite: 1]

        // 5. חיבור הטקסט ל-Controller
        controller.clockText = txtTimer;

        Debug.Log("Digital Clock created successfully!"); //[cite: 1]
    }

    // פונקציית עזר ליצירת טקסט[cite: 1]
    private TextMeshProUGUI CreateText(GameObject parent, string name, string content, Vector2 position, float fontSize, Color color) //[cite: 1]
    {
        GameObject textObj = new GameObject(name); //[cite: 1]
        textObj.transform.SetParent(parent.transform, false); //[cite: 1]

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>(); //[cite: 1]
        tmp.text = content; //[cite: 1]
        tmp.fontSize = fontSize; //[cite: 1]
        tmp.color = color; //[cite: 1]
        tmp.alignment = TextAlignmentOptions.Center; //[cite: 1]

        // הגדרת גודל התיבה[cite: 1]
        tmp.rectTransform.sizeDelta = new Vector2(300, 150); //[cite: 1]
        tmp.rectTransform.anchoredPosition = position; //[cite: 1]

        return tmp; //[cite: 1]
    }
}