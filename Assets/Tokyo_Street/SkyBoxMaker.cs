using UnityEngine;

public class SkyCamCapture : MonoBehaviour
{
    public Camera skyCamera;
    public int resolution = 1024;
    private string folderPath;

    void Start()
    {
        folderPath = Application.dataPath + "/SkyboxCaptures/";
        System.IO.Directory.CreateDirectory(folderPath);

        skyCamera.fieldOfView = 90;
        skyCamera.aspect = 1.0f;

        // Disable HDR on camera
        skyCamera.allowHDR = false;

        CaptureSkyCam();
    }

    void CaptureSkyCam()
    {
        Vector3[] rotations = new Vector3[]
        {
            new Vector3(0, 0, 0),     // Forward (Z+)
            new Vector3(0, 180, 0),   // Back (Z-)
            new Vector3(0, 90, 0),    // Right (X+)
            new Vector3(0, 270, 0),   // Left (X-)
            new Vector3(-90, 0, 0),   // Up (Y+)
            new Vector3(90, 0, 0)     // Down (Y-)
        };

        string[] fileNames = new string[]
        {
            "front",
            "back",
            "right",
            "left",
            "up",
            "down"
        };

        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        skyCamera.targetTexture = rt;

        for (int i = 0; i < 6; i++)
        {
            skyCamera.transform.eulerAngles = rotations[i];

            skyCamera.Render();
            RenderTexture.active = rt;

            Texture2D screenshot = new Texture2D(resolution, resolution, TextureFormat.RGB24, false, false);
            screenshot.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            screenshot.Apply();

            byte[] bytes = screenshot.EncodeToPNG();
            string filename = folderPath + fileNames[i] + ".png";
            System.IO.File.WriteAllBytes(filename, bytes);

            Destroy(screenshot);
        }

        skyCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);

        Debug.Log("Skybox captures saved to: " + folderPath);
    }
}