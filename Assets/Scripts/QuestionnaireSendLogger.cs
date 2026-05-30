using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class QuestionnaireSendLogger : MonoBehaviour
{
    [Header("Data Source")]
    [SerializeField] private ToggleGroup toggleGroup;

    [Tooltip("Enter the 21 answers here (-10 to 10). The order must match your UI toggles.")]
    [SerializeField] private List<string> possibleAnswers = new List<string>();

    [Header("Output")]
    [SerializeField] private string folderName = "QuestionnaireLogs";

    [Tooltip("Select the study condition for this session.")]
    [SerializeField] private StudyCondition condition = StudyCondition.MAN;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    public enum StudyCondition { MAN, WOMAN, MULTI }

    private int _questionIndex = 1;
    private int _selectedIndex = -1;
    private string _fileName;

    private string FolderPath => Path.Combine(Application.dataPath, folderName);
    private string FilePath => Path.Combine(FolderPath, _fileName);

    private void Awake()
    {
        if (toggleGroup == null)
            toggleGroup = GetComponentInChildren<ToggleGroup>(true);

        SetupDirectory();
        _fileName = ResolveFileName();

        if (debugLogs)
            Debug.Log($"[Questionnaire] Using file: {_fileName}");

        HookToggles();
        ResetForNextQuestion();
    }

    // -----------------------------------------------------------------------
    // Scans existing files in the folder to find the next participant number.
    // e.g. if answerMAN1.txt and answerMAN2.txt exist, returns answerMAN3.txt
    // -----------------------------------------------------------------------
    private string ResolveFileName()
    {
        string prefix = $"answer{condition}";
        string pattern = $@"^{Regex.Escape(prefix)}(\d+)\.txt$";
        int maxIndex = 0;

        if (Directory.Exists(FolderPath))
        {
            foreach (string file in Directory.GetFiles(FolderPath, $"{prefix}*.txt"))
            {
                string name = Path.GetFileName(file);
                Match m = Regex.Match(name, pattern);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int idx))
                    maxIndex = Math.Max(maxIndex, idx);
            }
        }

        return $"{prefix}{maxIndex + 1}.txt";
    }

    private void HookToggles()
    {
        if (toggleGroup == null) return;

        var toggles = toggleGroup.GetComponentsInChildren<Toggle>(true);

        for (int i = 0; i < toggles.Length; i++)
        {
            int index = i;
            toggles[i].onValueChanged.AddListener(isOn =>
            {
                if (isOn)
                {
                    _selectedIndex = index;
                    if (debugLogs && index < possibleAnswers.Count)
                        Debug.Log($"[Questionnaire] Selected Index {index}: Value is {possibleAnswers[index]}");
                }
            });
        }
    }

    public void OnSendPressed()
    {
        if (_selectedIndex == -1 || _selectedIndex >= possibleAnswers.Count)
        {
            Debug.LogWarning("[Questionnaire] No valid selection made.");
            return;
        }

        string answer = possibleAnswers[_selectedIndex];

        // Timestamp with 3 decimal places (milliseconds), space between date and time
        string timeUtcIso = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");

        string line = $"{_questionIndex} \t {timeUtcIso} \t {answer}{Environment.NewLine}";

        try
        {
            Directory.CreateDirectory(FolderPath);
            if (!File.Exists(FilePath))
                File.WriteAllText(FilePath, "q_index\tutc_time\tanswer\n", Encoding.UTF8);

            File.AppendAllText(FilePath, line, Encoding.UTF8);

#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
        }
        catch (Exception e)
        {
            Debug.LogError($"[Questionnaire] Write Failed: {e.Message}");
        }

        _questionIndex++;
        ResetForNextQuestion();
    }

    private void ResetForNextQuestion()
    {
        _selectedIndex = -1;
        if (toggleGroup == null) return;

        foreach (var t in toggleGroup.GetComponentsInChildren<Toggle>(true))
            t.SetIsOnWithoutNotify(false);
    }

    private void SetupDirectory()
    {
        if (!Directory.Exists(FolderPath))
        {
            Directory.CreateDirectory(FolderPath);
#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
        }
    }

    public void OnQuitExperiment()
    {
        string separator = "\n" +
                           "=================================================\n" +
                           $"   SESSION ENDED: {DateTime.Now}\n" +
                           "=================================================\n" +
                           "\n";
        try
        {
            File.AppendAllText(FilePath, separator, Encoding.UTF8);
            Debug.Log("[Questionnaire] Session separator saved.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Questionnaire] Could not save separator: {e.Message}");
        }

        _questionIndex = 1;
        QuitApplication();
    }

    private void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}