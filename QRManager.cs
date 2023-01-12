using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZXing;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Globalization;
using System;

public class QRManager : MonoBehaviour
{
    [SerializeField] private GameObject arObjectOnQRCode; // reference to the AR Gameobject which needs to be placed on scanning the QRCode

    [SerializeField] private Text info; // info text to display the url of the scanned QR-code

    [SerializeField] private Transform resultTransform; // "Result" is an parent object to hold the position and rotation data of the children

    private GameObject[] resultChilds = new GameObject[3]; // there are three childs: resulting object, loading sign and the error message

    private MeshFilter resultMeshFilter; // component holding the mesh

    private IBarcodeReader reader; // QRCode reading library

    // both part of Unity's ARFoundation
    private ARCameraManager aRCamera;
    private ARRaycastManager arRaycastManager;

    private Texture2D arCameraTexture; // texture to hold the processed AR Camera frame

    private string qrContent;

    private void Start()
    {
        resultMeshFilter = resultTransform.GetChild(1).GetComponent<MeshFilter>();

        // get resultChilds in the right order
        for (int i = 0; i < 3; i++) resultChilds[i] = resultTransform.GetChild(i).gameObject;

        reader = new BarcodeReader();
    }

    private struct ObjectFile
    {
        public string o; // name
        public List<Vector3> v; // vertices
        public List<int> f; // faces
    }

    // create ObjectFile from the text downloaded from the QR
    private static ObjectFile ReadObjectFile(string text)
    {
        ObjectFile obj = new ObjectFile();
        string[] lines = text.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        obj.v = new List<Vector3>();
        obj.f = new List<int>();

        // wavefront-files use the a dot as decimal seperator, but my standard C#-configurations does not
        CultureInfo culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ".";

        // go through all lines to get the necessary content
        foreach (string line in lines)
        {
            if (line == "" || line.StartsWith("#"))
                continue;

            string[] token = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries); // get all tokens of current line

            if (token.Length <= 0) continue;

            // first token is an indicator for what the line describes (ref: https://de.wikipedia.org/wiki/Wavefront_OBJ#Details)
            switch (token[0])
            {
                case ("o"): // add name
                    obj.o = token[1];
                    break;
                case ("v"): // add vertex
                    obj.v.Add(new Vector3(
                        float.Parse(token[1], culture),
                        float.Parse(token[2], culture),
                        float.Parse(token[3], culture)));
                    break;
                case ("f"): // add faces
                    // vertices of faces in wavefront can be written with '/' and some numbers for texture-coordinates or normals.
                    // They are not needed for this project so I just split at the '/' and take the first integer for vertices only ...
                    // ...(ref: 'token[1].Split('/')[0]')
                    // Wavefront begins counting its vertices at 1, but Lists begin at 0 (ref: '- 1')
                    int firstVertex = int.Parse(token[1].Split('/')[0]) - 1;

                    // split face into triangles if it has more than three vertices
                    for (int tri = 2; tri < token.Length - 1; tri++)
                    {
                        obj.f.Add(firstVertex);

                        obj.f.Add(int.Parse(token[tri].Split('/')[0]) - 1);
                        obj.f.Add(int.Parse(token[tri + 1].Split('/')[0]) - 1);
                    }
                    break;
            }
        }

        return obj;
    }

    // a scale factor that prevents the object from being to small or to huge, but always the same size at its widest axis
    private float ScaleFactor(float size, Vector3[] vertices, out float yOffset)
    {
        Vector3 maxValues = Vector3.zero;
        Vector3 minValues = Vector3.zero;

        foreach (Vector3 vertex in vertices)
        {
            for (int i = 0; i < 3; i++) // for all three axis
            {
                if (vertex[i] > maxValues[i]) maxValues[i] = vertex[i]; // get highest value of all vertices at given axis
                else if (vertex[i] < minValues[i]) minValues[i] = vertex[i]; // get lowest value of all vertices at given axis
            }
        }

        float maxDif = 0;

        for (int i = 0; i < 3; i++) // for all three axis
        {
            float dif = maxValues[i] - minValues[i]; // get difference of both extreme values

            if (dif > maxDif) maxDif = dif; // get highest of all differences
        }

        float scaleFactor = size / maxDif;

        yOffset = minValues.y * scaleFactor * -1f; // yOffset, so that the object is standing on and not in or above the ground
        return scaleFactor;
    }

    private void SetResultChildActive(int j) // set specific resultChild active and disable the other ones
    {
        for (int i = 0; i < 3; i++) resultChilds[i].SetActive(i == j);
    }

    private void OnEnable()
    {
        if (!aRCamera)
        {
            aRCamera = FindObjectOfType<ARCameraManager>(); //Load the ARCamera
            arRaycastManager = FindObjectOfType<ARRaycastManager>(); //Load the Raycast Manager
        }

        aRCamera.frameReceived += OnCameraFrameReceived;
    }

    private void OnDisable() => aRCamera.frameReceived -= OnCameraFrameReceived;

    private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if ((Time.frameCount % 15) == 0)
        { // you can set this number based on the frequency to scan for QRCode
            XRCpuImage image;
            if (aRCamera.TryAcquireLatestCpuImage(out image))
            {
                StartCoroutine(ProcessQRCode(image));
                image.Dispose();
            }
        }
    }

    private void SplitVertices(ref Vector3[] vertices, ref int[] faces)
    {
        List<Vector3> tempVertices = new List<Vector3>();
        List<int> tempFaces = new List<int>();

        for (int i = 0; i < faces.Length; i++) // for each index in faces take its vertices and copy them in the new vertices list
        {
            tempVertices.Add(vertices[faces[i]]);

            tempFaces.Add(i); // then save the new index in a new faces-list
        }

        // replace old arrays with new ones
        vertices = tempVertices.ToArray();
        faces = tempFaces.ToArray();
    }

    private IEnumerator GetMesh()
    {
        UnityWebRequest www = UnityWebRequest.Get(qrContent.Trim());
        yield return www.SendWebRequest(); // send and wait for respond

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.Log(www.error);

            SetResultChildActive(2); // set error message active
        }
        else
        {
            try // in this step a lot can go wrong
            {
                // show results as text
                string text = www.downloadHandler.text;
                Debug.Log(text);

                ObjectFile obj = ReadObjectFile(text); // convert text to usable ObjectFile

                Mesh mesh = new Mesh();

                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // make sure there are enough vertices even for more complex objects

                int[] faces = obj.f.ToArray();

                Vector3[] vertices = obj.v.ToArray();

                // scale stuff so it won't be to big
                float yOffset, // offset gets calculated wit the scalefactor
                    scalefactor = ScaleFactor(.4f, vertices, out yOffset);

                // so it looks lowpoly and gets sharp edges
                SplitVertices(ref vertices, ref faces);

                // resultChilds[1] is the child containing the MeshFilter
                resultChilds[1].transform.localPosition = Vector3.up * yOffset;

                resultChilds[1].transform.localScale = Vector3.one * scalefactor;

                mesh.name = obj.o;
                mesh.vertices = vertices;
                mesh.triangles = faces;

                mesh.RecalculateNormals();
                mesh.Optimize();

                resultMeshFilter.mesh = mesh;

                SetResultChildActive(1); // set mesh active
            }
            catch (Exception)
            { // if it failed here, it will show a generic error message in AR
                SetResultChildActive(2);
            }
        }
    }

    private IEnumerator ProcessQRCode(XRCpuImage image)
    {
        // Create the async conversion request
        XRCpuImage.AsyncConversion request = image.ConvertAsync(new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, image.width, image.height),
            outputDimensions = new Vector2Int(image.width / 2, image.height / 2),
            // Color image format
            outputFormat = TextureFormat.RGB24,
        });

        while (!request.status.IsDone())
            yield return null; // wait for process to be finished

        // Check status to see if it completed successfully.
        if (request.status != XRCpuImage.AsyncConversionStatus.Ready)
        {
            // Something went wrong
            Debug.LogErrorFormat("Request failed with status {0}", request.status);
            // Dispose even if there is an error.
            request.Dispose();
            yield break;
        }

        // apply it to a Texture2D
        var rawData = request.GetData<byte>();

        // create a texture if necessary
        if (arCameraTexture == null)
        {
            arCameraTexture = new Texture2D(
            request.conversionParams.outputDimensions.x,
            request.conversionParams.outputDimensions.y,
            request.conversionParams.outputFormat,
            false);
        }

        // copy the image data into the texture
        arCameraTexture.LoadRawTextureData(rawData);
        arCameraTexture.Apply();
        byte[] barcodeBitmap = arCameraTexture.GetRawTextureData();

        LuminanceSource source = new RGBLuminanceSource(barcodeBitmap, arCameraTexture.width, arCameraTexture.height);
        //Send the source to decode the QRCode using ZXing
        Result result = reader.Decode(source);

        if (result != null && result.Text != "" && result.Text != qrContent)
        { // if QRCode found inside the frame
            qrContent = result.Text;

            Debug.Log(qrContent);
            info.text = qrContent; // set 

            // Now let's determine the QR-Codes position on the screen

            // Get the resultsPoints of each qr code contain the following points in the following order:
            // index 0: bottomLeft index 1: topLeft index 2: topRight
            // Note this depends on the oreintation of the QRCode. The below part is mainly finding the mid of the QRCode ...
            // ... using result points and making a raycast hit from that pose.
            ResultPoint[] resultPoints = result.ResultPoints;
            ResultPoint b = resultPoints[0];
            ResultPoint c = resultPoints[2];

            // a lot of the following calculations is used to make the coordinates given by 'result.ResultPoints' to actually usable coordinates
            float x = image.height / 2f;
            float y = image.width / 2f;
            float z = x / 2f - (Screen.width * y) / (2f * Screen.height);
            // pos1: bottomLeft; pos2: topRight; pos4: (kinda) mid
            Vector2 pos1 = new Vector2((1 - ((float)b.Y - z) / (x - 2 * z)) * Screen.width, (1 - (float)b.X / y) * Screen.height);
            Vector2 pos2 = new Vector2((1 - ((float)c.Y - z) / (x - 2 * z)) * Screen.width, (1 - (float)c.X / y) * Screen.height);
            Vector2 pos4 = (pos1 + pos2) / 2f;

            // Now let's determine the QR-Codes position in the AR-World
            List<ARRaycastHit> aRRaycastHits = new List<ARRaycastHit>();
            // make a raycast hit to get the pos of the QRCode detected to place an object on it.
            if (arRaycastManager.Raycast(pos4, aRRaycastHits, TrackableType.FeaturePoint) && aRRaycastHits.Count > 0)
            {
                if (!resultTransform.gameObject.activeSelf) resultTransform.gameObject.SetActive(true); // Activate result parent

                resultTransform.position = aRRaycastHits[0].pose.position; // set result parent transforms
                resultTransform.rotation = aRRaycastHits[0].pose.rotation;

                SetResultChildActive(0); // activate AR-loading-sign

                StartCoroutine(GetMesh()); // now that a QR-code has been detected and its position in the AR-World has been determined...
                // ...we can get the mesh!
            }
            else
            {
                qrContent = ""; // reset qrContent if no mesh could be instantiated 'cause of no raycast hit
            }
        }
    }
}
