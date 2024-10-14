using CefSharp.Wpf;
using Microsoft.Kinect;
using Newtonsoft.Json;
using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace KinectControllerApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static FrameDescription depthFrameDesc;
        private static MultiSourceFrameReader multiFrameSourceReader;

        private static ushort[] depthFrameData;
        private static byte[] bodyIndexFrameData;

        private static KinectSensor sensor;

        private static SocketIOClient.SocketIO io;

        private static System.Timers.Timer timer;

        private static byte[] compressedKinectData;

        private static Image kinectImage;
        private static Image overlayImage1;
        private static Image overlayImage2;
        private static Grid mainGrid;
        private static ChromiumWebBrowser browser;

        private static Canvas movementCanvas;
        private static Ellipse movementRef;
        private static Ellipse movementVis;
        private static bool movementInteraction;

        private static Canvas rotationCanvas;
        private static Ellipse rotationRef;
        private static Ellipse rotationVis;
        private static bool rotationInteraction;

        private static BitmapSource kinectSource;

        private static TextBox console;

        private static string debugString = "";

        private static Vector finalMovementVec = new Vector();
        private static Vector finalRotationVec = new Vector();

        private static TextBox posTBX;
        private static TextBox posTBY;
        private static TextBox rotTBX;

        private static Label connectionStatus;

        private static CancellationTokenSource cts = new CancellationTokenSource();

        private struct Vector2
        {
            [JsonProperty]
            public double x;
            [JsonProperty]
            public double y;
        }

        private struct KinectTransform
        {
            [JsonProperty]
            public Vector2 position;
            [JsonProperty]
            public Vector2 rotation;
        }

        public MainWindow()
        {
            InitializeComponent();
            kinectImage = KinectImage;
            overlayImage1 = OverlayImage1;
            overlayImage2 = OverlayImage2;
            mainGrid = MainGrid;
            console = consoleTB;
            browser = GameWindow;

            movementCanvas = MovementCanvas;
            movementRef = MovementRef;
            movementVis = MovementVis;
            movementInteraction = false;

            rotationCanvas = RotationCanvas;
            rotationRef = RotationRef;
            rotationVis = RotationVis;
            rotationInteraction = false;

            posTBX = PosTBX; 
            posTBY = PosTBY;
            rotTBX = RotTBX;

            connectionStatus = ConnectionStatusLabel; 

            Init();
        }

        static void Init()
        {
            io = new SocketIOClient.SocketIO("http://localhost:3001/", new SocketIOOptions
            //io = new SocketIOClient.SocketIO("http://localhost:3001/", new SocketIOOptions
            {
                Query = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("token", "Nazia.CAM.Project"),
                    new KeyValuePair<string, string>("secret", "ezpass")
                },
            });

            io.On("kinectTransform", res =>
            {
                debugString = res.ToString();
                try 
                {
                    KinectTransform m = JsonConvert.DeserializeObject<KinectTransform>(res.GetValue<string>(0));
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        posTBX.Text = m.position.x.ToString();
                        posTBY.Text = m.position.y.ToString();
                        rotTBX.Text = m.rotation.x.ToString();
                    });
                }
                catch (Exception ex) 
                {
                    debugString = ex.Message;
                }
            });

            sensor = KinectSensor.GetDefault();

            if (sensor != null)
            {
                multiFrameSourceReader = sensor.OpenMultiSourceFrameReader(FrameSourceTypes.Depth | FrameSourceTypes.Color | FrameSourceTypes.BodyIndex | FrameSourceTypes.Body);

                multiFrameSourceReader.MultiSourceFrameArrived += Reader_MultiSourceFrameArrived;

                depthFrameDesc = sensor.DepthFrameSource.FrameDescription;
                int depthWidth = depthFrameDesc.Width;
                int depthHeight = depthFrameDesc.Height;
                bodyIndexFrameData = new byte[depthWidth * depthHeight];
                depthFrameData = new ushort[depthWidth * depthHeight];

                if (!sensor.IsOpen)
                    sensor.Open();

                timer = new System.Timers.Timer
                {
                    Interval = 100,
                    AutoReset = false
                };
                timer.Elapsed += Timer_Elapsed;
            }
        }

        private static async void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // Check if cancellation has been requested, and exit early if so
            if (cts == null || cts.Token.IsCancellationRequested) return;

            StringBuilder finalText = new StringBuilder();
            try
            {
                if (sensor.IsOpen && !io.Connected)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        connectionStatus.Content = "Status: ❎";
                        connectionStatus.Foreground = Brushes.Red;
                    });
                    finalText.AppendLine("Camera Active and Server Disconnected!");
                    await io.ConnectAsync();
                }
                else if (sensor.IsOpen && io.Connected)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        connectionStatus.Content = "Status: ✅";
                        connectionStatus.Foreground = Brushes.Green;
                    });
                    finalText.AppendLine("Camera Active and Server Connected!");
                    finalText.AppendLine($"Sending {compressedKinectData.Length / 1024.0:F2} kilobytes of point data.");
                    await io.EmitAsync("kdata", compressedKinectData);
                    if (movementInteraction)
                        await io.EmitAsync("kmov", JsonConvert.SerializeObject(new Vector2() { x = finalMovementVec.X, y = finalMovementVec.Y }));
                    if (rotationInteraction)
                        await io.EmitAsync("krot", JsonConvert.SerializeObject(new Vector2() { x = finalRotationVec.X, y = 0 }));
                }
                else finalText.AppendLine("Camera Inactive and Server Disconnected!");

                if (debugString != null && debugString.Length > 0)
                    finalText.AppendLine(debugString);

                finalText.AppendLine($"Movement Vec:\n\tX: {finalMovementVec.X}\n\tY: {finalMovementVec.Y}");
                finalText.AppendLine($"Rotation Vec:\n\tX: {finalRotationVec.X}");

                //Clear Vecs
                if (!movementInteraction)
                {
                    finalMovementVec.X = 0;
                    finalMovementVec.Y = 0;
                }
                if (!rotationInteraction)
                {
                    finalRotationVec.X = 0;
                    finalRotationVec.Y = 0;
                }

                // Use Dispatcher to safely update the UI
                if (Application.Current?.Dispatcher != null)
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (console != null && finalText != null)
                            console.Text = finalText.ToString();

                        Canvas.SetLeft(movementRef, movementCanvas.ActualWidth / 2 - movementRef.ActualWidth / 2);
                        Canvas.SetTop(movementRef, movementCanvas.ActualHeight / 2 - movementRef.ActualHeight / 2);
                        if (movementInteraction == false)
                        {
                            Canvas.SetLeft(movementVis, movementCanvas.ActualWidth / 2 - movementVis.ActualWidth / 2);
                            Canvas.SetTop(movementVis, movementCanvas.ActualHeight / 2 - movementVis.ActualHeight / 2);
                        }

                        Canvas.SetLeft(rotationRef, rotationCanvas.ActualWidth / 2 - rotationRef.ActualWidth / 2);
                        Canvas.SetTop(rotationRef, rotationCanvas.ActualHeight / 2 - rotationRef.ActualHeight / 2);
                        Canvas.SetTop(rotationVis, rotationCanvas.ActualHeight / 2 - rotationVis.ActualHeight / 2);
                        if (rotationInteraction == false)
                        {
                            Canvas.SetLeft(rotationVis, rotationCanvas.ActualWidth / 2 - rotationVis.ActualWidth / 2);
                        }
                    });

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private static void Reader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            if (cts == null || cts.Token.IsCancellationRequested) return;
            bool multiSourceFrameProcessed = false;
            bool bodyIndexFrameProcessed = false;
            bool bodyFrameProcessed = false;
            bool depthFrameProcessed = false;

            MultiSourceFrame multiSourceFrame = e.FrameReference.AcquireFrame();

            if (sensor == null) return;
            Vector4 floorPlane = new Vector4();

            if (multiSourceFrame != null)
            {
                using (BodyFrame bodyFrame = multiSourceFrame.BodyFrameReference.AcquireFrame())
                using (BodyIndexFrame bodyIndexFrame = multiSourceFrame.BodyIndexFrameReference.AcquireFrame())
                using (DepthFrame depthFrame = multiSourceFrame.DepthFrameReference.AcquireFrame())
                using (ColorFrame colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame())
                {
                    if (bodyFrame != null)
                    {
                        floorPlane = bodyFrame.FloorClipPlane;
                        bodyFrameProcessed = true;
                    }

                    if (bodyIndexFrame != null)
                    {
                        bodyIndexFrame.CopyFrameDataToArray(bodyIndexFrameData);
                        bodyIndexFrameProcessed = true;
                    }

                    if (depthFrame != null)
                    {
                        depthFrame.CopyFrameDataToArray(depthFrameData);
                        depthFrameProcessed = true;

                        // Find the min and max depth values in the frame
                        ushort minDepth = ushort.MaxValue;
                        ushort maxDepth = ushort.MinValue;

                        foreach (ushort depth in depthFrameData)
                        {
                            if (depth > 0) // Ignore zero (no data)
                            {
                                if (depth < minDepth) minDepth = depth;
                                if (depth > maxDepth) maxDepth = depth;
                            }
                        }

                        // Create an array for the 8-bit reversed grayscale image
                        byte[] depthFrameReversedData = new byte[depthFrame.FrameDescription.Width * depthFrame.FrameDescription.Height];

                        for (int i = 0; i < depthFrameData.Length; i++)
                        {
                            ushort depth = depthFrameData[i];

                            // Ignore depth values of 0 (no data)
                            if (depth == 0)
                            {
                                depthFrameReversedData[i] = 0; // Set no data as black
                            }
                            else
                            {
                                // Reverse the depth: close objects are bright, far objects are dark
                                depthFrameReversedData[i] = (byte)(255 * (1 - ((float)(depth - minDepth) / (maxDepth - minDepth))));
                            }
                        }

                        kinectSource = BitmapSource.Create(
                            depthFrame.FrameDescription.Width,
                            depthFrame.FrameDescription.Height,
                            0, 0, PixelFormats.Gray8, null,
                            depthFrameReversedData, (int)(depthFrame.FrameDescription.Width)
                        );

                        kinectImage.Source = kinectSource;

                        if ((int)mainGrid.ActualWidth > 0)
                        {
                            overlayImage2.Source = kinectSource;
                            OverlayElement(browser);
                        }
                    }

                    //if (colorFrame != null){}

                    multiSourceFrameProcessed = true;
                }

                if (multiSourceFrameProcessed && depthFrameProcessed && bodyIndexFrameProcessed && bodyFrameProcessed)
                {
                    int originalWidth = depthFrameDesc.Width;
                    int originalHeight = depthFrameDesc.Height;

                    int reductionFactorX = 3;
                    int reductionFactorY = 3;

                    // Calculate the new reduced dimensions, ensuring they're integers
                    int reducedWidth = originalWidth / reductionFactorX;
                    int reducedHeight = originalHeight / reductionFactorY;

                    // Create arrays to hold the reduced rows and columns
                    ushort[] reducedDepthFrameData = new ushort[reducedWidth * reducedHeight];
                    byte[] reducedBodyIndexFrameData = new byte[reducedWidth * reducedHeight];

                    // Copy data with the variable reduction factor
                    for (int y = 0; y < originalHeight; y += reductionFactorY)
                    {
                        for (int x = 0; x < originalWidth; x += reductionFactorX)
                        {
                            int originalIndex = (y * originalWidth) + x;
                            int reducedIndex = (y / reductionFactorY) * reducedWidth + (x / reductionFactorX);

                            if (reducedIndex < reducedDepthFrameData.Length)
                            {
                                reducedDepthFrameData[reducedIndex] = depthFrameData[originalIndex];
                                reducedBodyIndexFrameData[reducedIndex] = bodyIndexFrameData[originalIndex];
                            }
                        }
                    }

                    CameraIntrinsics c = sensor.CoordinateMapper.GetDepthCameraIntrinsics();
                    compressedKinectData = CombineAndCompressFrames(
                        reducedDepthFrameData,
                        reducedBodyIndexFrameData,
                        reducedWidth,
                        reducedHeight,  // Adjusted dimensions for correct aspect ratio
                        c.FocalLengthX / reductionFactorX,
                        c.FocalLengthY / reductionFactorY,
                        c.PrincipalPointX / reductionFactorX,
                        c.PrincipalPointY / reductionFactorY,
                        floorPlane
                    );

                    if (timer.Enabled == false) timer.Start();
                }
            }
        }

        private static byte[] CombineAndCompressFrames(
        ushort[] depthFrameData,
        byte[] bodyIndexFrameData,
        int depthWidth,
        int depthHeight,
        float focalLengthX,
        float focalLengthY,
        float principalPointX,
        float principalPointY,
        Vector4 floorClipPlane)
        {
            // Convert ushort[] depthFrameData to byte[]
            byte[] depthFrameBytes = new byte[depthFrameData.Length * sizeof(ushort)];
            Buffer.BlockCopy(depthFrameData, 0, depthFrameBytes, 0, depthFrameBytes.Length);

            // Allocate space for metadata and all the frame data
            using (var memoryStream = new MemoryStream())
            {
                using (var binaryWriter = new BinaryWriter(memoryStream))
                {
                    // Write the lengths of each array first (metadata)
                    binaryWriter.Write(depthFrameBytes.Length);
                    binaryWriter.Write(bodyIndexFrameData.Length);

                    // Write the depth frame width and height
                    binaryWriter.Write(depthWidth);
                    binaryWriter.Write(depthHeight);

                    // Write the intrinsic parameters
                    binaryWriter.Write(focalLengthX);
                    binaryWriter.Write(focalLengthY);
                    binaryWriter.Write(principalPointX);
                    binaryWriter.Write(principalPointY);

                    // Write the floor clip plane
                    binaryWriter.Write(floorClipPlane.X);
                    binaryWriter.Write(floorClipPlane.Y);
                    binaryWriter.Write(floorClipPlane.Z);
                    binaryWriter.Write(floorClipPlane.W);

                    // Write the actual frame data
                    binaryWriter.Write(depthFrameBytes);
                    binaryWriter.Write(bodyIndexFrameData);
                }

                // Compress the combined byte array
                return CompressData(memoryStream.ToArray());
            }
        }

        private static byte[] CompressData(byte[] data)
        {
            using (var outputStream = new MemoryStream())
            {
                using (var compressionStream = new DeflateStream(outputStream, CompressionLevel.Optimal))
                {
                    compressionStream.Write(data, 0, data.Length);
                }
                return outputStream.ToArray();
            }
        }

        public static void OverlayElement(UIElement sourceElement)
        {
            // Get the actual size of the source
            double actualHeight = Math.Max(sourceElement.RenderSize.Height, 1);
            double actualWidth = Math.Max(sourceElement.RenderSize.Width, 1);

            // Calculate scaled dimensions
            double renderHeight = actualHeight;
            double renderWidth = actualWidth;

            // Calculate crop area
            int cropStartX = (int)0;
            int cropEndX = (int)renderWidth;
            int cropStartY = (int)0;
            int cropEndY = (int)renderHeight;

            int cropWidth = cropEndX - cropStartX;
            int cropHeight = cropEndY - cropStartY;

            // Scale factors to fit maxWidth and maxHeight
            int maxWidth = 640;
            int maxHeight = 480;
            double scaleX = (double)maxWidth / cropWidth;
            double scaleY = (double)maxHeight / cropHeight;
            double scale = Math.Min(scaleX, scaleY);

            int finalWidth = (int)(cropWidth * scale);
            int finalHeight = (int)(cropHeight * scale);

            // Create a single RenderTargetBitmap
            RenderTargetBitmap renderTarget = new RenderTargetBitmap(finalWidth, finalHeight, 96, 96, PixelFormats.Pbgra32);
            // Create a drawing visual and apply transformations directly
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(scale, scale));
                context.PushTransform(new TranslateTransform(-cropStartX, -cropStartY));
                context.DrawRectangle(new VisualBrush(sourceElement), null, new Rect(new Point(0, 0), new Size(actualWidth, actualHeight)));
            }
            renderTarget.Render(visual);

            overlayImage1.Source = renderTarget;
        }

        private void OnMovementCanvasClickDown(object sender, MouseButtonEventArgs e)
        {
            movementInteraction = true;
        }

        private void OnRotationCanvasClickDown(object sender, MouseButtonEventArgs e)
        {
            rotationInteraction = true;
        }

        private void WindowMouseUp(object sender, MouseButtonEventArgs e)
        {
            movementInteraction = false;
            rotationInteraction = false;
        }

        private void WindowMouseMove(object sender, MouseEventArgs e)
        {
            if (movementInteraction)
            {
                Point mouseCoords = Mouse.GetPosition(movementCanvas);

                Point canvasCenter = new Point(movementCanvas.ActualWidth / 2, movementCanvas.ActualHeight / 2);
                Vector mouseVector = mouseCoords - canvasCenter;

                double length = mouseVector.Length;
                double smallestSide = (movementCanvas.ActualWidth < movementCanvas.ActualHeight) ? movementCanvas.ActualWidth / 2 - 6 : movementCanvas.ActualHeight / 2 - 6;
                if (length > smallestSide)
                {
                    mouseVector.X /= length;
                    mouseVector.Y /= length;
                    mouseVector *= smallestSide;
                }
                Canvas.SetLeft(movementVis, mouseVector.X + canvasCenter.X - movementVis.ActualWidth / 2);
                Canvas.SetTop(movementVis, mouseVector.Y + canvasCenter.Y - movementVis.ActualHeight / 2);
                try
                {
                    //Counter Clockwise
                    //x' = x * cos(θ) - y * sin(θ)
                    //y' = x * sin(θ) + y * cos(θ)
                    //Clockwise
                    //x' = x * cos(θ) + y * sin(θ)
                    //y' = -x * sin(θ) + y * cos(θ)
                    mouseVector.X /= 10;
                    mouseVector.Y /= 10;
                    double theta = (Math.PI / 180) * double.Parse(RotTBX.Text);
                    double tempX = mouseVector.X / canvasCenter.X;
                    double tempY = -(mouseVector.Y / canvasCenter.Y);
                    finalMovementVec.X = tempX * Math.Cos(theta) + tempY * Math.Sin(theta);
                    finalMovementVec.Y = -tempX * Math.Sin(theta) + tempY * Math.Cos(theta);
                }
                catch 
                {
                    finalMovementVec.X = 0;
                    finalMovementVec.Y = 0;
                }
            }

            if (rotationInteraction)
            {
                Point mouseCoords = Mouse.GetPosition(rotationCanvas);

                Point canvasCenter = new Point(rotationCanvas.ActualWidth / 2, rotationCanvas.ActualHeight / 2);
                Vector mouseVector = new Point(mouseCoords.X, 0) - canvasCenter;

                double length = mouseVector.Length;
                double smallestSide = rotationCanvas.ActualWidth / 2 - 6;
                if (length > smallestSide)
                {
                    mouseVector.X /= length;
                    mouseVector.Y /= length;
                    mouseVector *= smallestSide;
                }
                Canvas.SetLeft(rotationVis, mouseVector.X + canvasCenter.X - rotationVis.ActualWidth / 2);
                finalRotationVec.X = mouseVector.X / canvasCenter.X;
                finalRotationVec.Y = 0;
            }
        }

        private async void SaveButtonClicked(object sender, RoutedEventArgs e)
        {
            await io.EmitAsync("savet");
        }

        private async void ApplyButtonClicked(object sender, RoutedEventArgs e)
        {
            KinectTransform t = new KinectTransform()
            {
                position = new Vector2 { x = double.Parse(PosTBX.Text), y = double.Parse(PosTBY.Text) },
                rotation = new Vector2 { x = double.Parse(RotTBX.Text), y = 0 }
            };
            await io.EmitAsync("applyt", JsonConvert.SerializeObject(t));
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            cts.Cancel();
            multiFrameSourceReader?.Dispose();

            if (timer != null)
            {
                timer.Elapsed -= Timer_Elapsed;
                timer.Stop();
                timer.Dispose();
            }

            await io?.DisconnectAsync();
            io?.Dispose();

            if (sensor != null && sensor.IsOpen)
            {
                sensor.Close();
            }
        }
    }
}