﻿//----------------------------------------------------------------------------
//  Copyright (C) 2004-2021 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Dnn;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Emgu.Util;

namespace Emgu.CV.Models
{
    /// <summary>
    /// DNN Vehicle license plate detector using OpenVino
    /// </summary>
    public class VehicleLicensePlateDetector : DisposableObject, IProcessAndRenderModel
    {
        /// <summary>
        /// License plate
        /// </summary>
        public struct LicensePlate
        {
            /// <summary>
            /// The region of the license plate
            /// </summary>
            public Rectangle Region;
            /// <summary>
            /// The text on the license plate
            /// </summary>
            public String Text;
        }

        /// <summary>
        /// Vehicle
        /// </summary>
        public class Vehicle
        {
            /// <summary>
            /// The vehicle region
            /// </summary>
            public Rectangle Region;
            /// <summary>
            /// The color of the vehicle
            /// </summary>
            public String Color;
            /// <summary>
            /// The vehicle type
            /// </summary>
            public String Type;
            /// <summary>
            /// The license plate. If null, there is no license plate detected.
            /// </summary>
            public LicensePlate? LicensePlate;

            /// <summary>
            /// If the license plate region is located within the vehicle region
            /// </summary>
            /// <param name="p">The license plate</param>
            /// <param name="plateOverlapRatio">A license plate is overlapped with the vehicle if the specific ratio of the license plate area is overlapped.</param>
            /// <returns>True if the license plate overlap with the vehicle.</returns>
            public bool ContainsPlate(LicensePlate p, double plateOverlapRatio = 0.8)
            {
                if (Region.IsEmpty || p.Region.IsEmpty)
                    return false;
                double plateSize = p.Region.Width * p.Region.Height;
                Rectangle overlap = p.Region;
                overlap.Intersect(Region);
                double overlapSize = overlap.Width * overlap.Height;
                if (overlapSize / plateSize < plateOverlapRatio)
                    return false;
                return true;
            }
        }

        private String _modelFolderName = "vehicle-license-plate-detection-barrier-0106-openvino-2021.2";

        private DetectionModel _vehicleLicensePlateDetectionModel = null;
        private Net _vehicleAttrRecognizer = null;
        private Net _ocr = null;

        private async Task InitOCR(System.Net.DownloadProgressChangedEventHandler onDownloadProgressChanged = null)
        {
            if (_ocr == null)
            {
                FileDownloadManager manager = new FileDownloadManager();

                manager.AddFile(
                    "https://storage.openvinotoolkit.org/repositories/open_model_zoo/2021.2/models_bin/3/license-plate-recognition-barrier-0001/FP32/license-plate-recognition-barrier-0001.xml",
                    _modelFolderName,
                    "B5B649B9566F5CF352554ACFFD44207F4AECEE1DA767F4B69F46060A102623FA");
                manager.AddFile(
                    "https://storage.openvinotoolkit.org/repositories/open_model_zoo/2021.2/models_bin/3/license-plate-recognition-barrier-0001/FP32/license-plate-recognition-barrier-0001.bin",
                    _modelFolderName,
                    "685934518A930CC55D023A53AC2D5E47BBE81B80828354D8318DE6DC3AD5CFBA");

                if (onDownloadProgressChanged != null)
                {
                    manager.OnDownloadProgressChanged += onDownloadProgressChanged;
                }

                await manager.Download();

                _ocr =
                    DnnInvoke.ReadNetFromModelOptimizer(manager.Files[0].LocalFile, manager.Files[1].LocalFile);

                using (Mat seqInd = new Mat(
                    new Size(1, 88),
                    DepthType.Cv32F,
                    1))
                {
                    if (seqInd.Depth == DepthType.Cv32F)
                    {
                        float[] seqIndValues = new float[seqInd.Width * seqInd.Height];
                        for (int j = 1; j < seqIndValues.Length; j++)
                        {
                            seqIndValues[j] = 1.0f;
                        }

                        seqIndValues[0] = 0.0f;
                        seqInd.SetTo(seqIndValues);
                    }

                    _ocr.SetInput(seqInd, "seq_ind");
                }

                /*
                if (Emgu.CV.Cuda.CudaInvoke.HasCuda)
                {
                    _ocr.SetPreferableBackend(Emgu.CV.Dnn.Backend.Cuda);
                    _ocr.SetPreferableTarget(Emgu.CV.Dnn.Target.Cuda);
                }*/
            }
        }

        private async Task InitVehicleAttributesRecognizer(System.Net.DownloadProgressChangedEventHandler onDownloadProgressChanged = null)
        {
            if (_vehicleAttrRecognizer == null)
            {
                FileDownloadManager manager = new FileDownloadManager();

                manager.AddFile(
                    "https://storage.openvinotoolkit.org/repositories/open_model_zoo/2021.2/models_bin/3/vehicle-attributes-recognition-barrier-0042/FP32/vehicle-attributes-recognition-barrier-0042.xml",
                    _modelFolderName,
                    "9D1E877B153699CAF4547D08BFF7FE268F65B663441A42B929924B8D95DACDBB");
                manager.AddFile(
                    "https://storage.openvinotoolkit.org/repositories/open_model_zoo/2021.2/models_bin/3/vehicle-attributes-recognition-barrier-0042/FP32/vehicle-attributes-recognition-barrier-0042.bin",
                    _modelFolderName,
                    "492520E55F452223E767D54227D6EF6B60B0C1752DD7B9D747BE65D57B685A0E");

                manager.OnDownloadProgressChanged += onDownloadProgressChanged;
                await manager.Download();
                _vehicleAttrRecognizer =
                    DnnInvoke.ReadNetFromModelOptimizer(manager.Files[0].LocalFile, manager.Files[1].LocalFile);

                /*
                if (Emgu.CV.Cuda.CudaInvoke.HasCuda)
                {
                    _vehicleAttrRecognizer.SetPreferableBackend(Emgu.CV.Dnn.Backend.Cuda);
                    _vehicleAttrRecognizer.SetPreferableTarget(Emgu.CV.Dnn.Target.Cuda);
                }*/
            }
        }

        private async Task InitLicensePlateDetector(System.Net.DownloadProgressChangedEventHandler onDownloadProgressChanged = null)
        {
            if (_vehicleLicensePlateDetectionModel == null)
            {
                FileDownloadManager manager = new FileDownloadManager();

                manager.AddFile(
                    "https://storage.openvinotoolkit.org/repositories/open_model_zoo/2021.2/models_bin/3/vehicle-license-plate-detection-barrier-0106/FP32/vehicle-license-plate-detection-barrier-0106.xml",
                    _modelFolderName);
                manager.AddFile(
                    "https://storage.openvinotoolkit.org/repositories/open_model_zoo/2021.2/models_bin/3/vehicle-license-plate-detection-barrier-0106/FP32/vehicle-license-plate-detection-barrier-0106.bin",
                    _modelFolderName);

                manager.OnDownloadProgressChanged += onDownloadProgressChanged;
                await manager.Download();

                _vehicleLicensePlateDetectionModel =
                    new DetectionModel(manager.Files[1].LocalFile, manager.Files[0].LocalFile);
                _vehicleLicensePlateDetectionModel.SetInputSize(new Size(300, 300));
                _vehicleLicensePlateDetectionModel.SetInputMean(new MCvScalar());
                _vehicleLicensePlateDetectionModel.SetInputScale(1.0);
                _vehicleLicensePlateDetectionModel.SetInputSwapRB(false);
                _vehicleLicensePlateDetectionModel.SetInputCrop(false);
                
                /*
                if (Emgu.CV.Cuda.CudaInvoke.HasCuda)
                {
                    _vehicleLicensePlateDetector.SetPreferableBackend(Emgu.CV.Dnn.Backend.Cuda);
                    _vehicleLicensePlateDetector.SetPreferableTarget(Emgu.CV.Dnn.Target.Cuda);
                }*/
            }
        }

        private String[] _colorName = new String[] { "white", "gray", "yellow", "red", "green", "blue", "black" };
        private String[] _vehicleType = new String[] { "car", "van", "truck", "bus" };
        private String[] _plateText = new string[]
        {
            "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
            "<Anhui>", "<Beijing>", "<Chongqing>", "<Fujian>",
            "<Gansu>", "<Guangdong>", "<Guangxi>", "<Guizhou>",
            "<Hainan>", "<Hebei>", "<Heilongjiang>", "<Henan>",
            "<HongKong>", "<Hubei>", "<Hunan>", "<InnerMongolia>",
            "<Jiangsu>", "<Jiangxi>", "<Jilin>", "<Liaoning>",
            "<Macau>", "<Ningxia>", "<Qinghai>", "<Shaanxi>",
            "<Shandong>", "<Shanghai>", "<Shanxi>", "<Sichuan>",
            "<Tianjin>", "<Tibet>", "<Xinjiang>", "<Yunnan>",
            "<Zhejiang>", "<police>",
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J",
            "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T",
            "U", "V", "W", "X", "Y", "Z"
        };


        /// <summary>
        /// Download and initialize the vehicle detector, the license plate detector and OCR.
        /// </summary>
        /// <param name="onDownloadProgressChanged">Callback when download progress has been changed</param>
        /// <returns>Async task</returns>
        public async Task Init(System.Net.DownloadProgressChangedEventHandler onDownloadProgressChanged = null)
        {
            await InitLicensePlateDetector(onDownloadProgressChanged);
            await InitVehicleAttributesRecognizer(onDownloadProgressChanged);
            await InitOCR(onDownloadProgressChanged);
        }

        public string ProcessAndRender(IInputArray imageIn, IInputOutputArray imageOut)
        {
            Stopwatch watch = Stopwatch.StartNew();
            Vehicle[] detectionResult = Detect(imageIn);
            watch.Stop();
            if (imageOut != imageIn)
            {
                using (InputArray iaImageIn = imageIn.GetInputArray())
                {
                    iaImageIn.CopyTo(imageOut);
                }
            }
            Render(imageOut, detectionResult);
            return String.Format("Detected in {0} milliseconds.", watch.ElapsedMilliseconds);
        }

        /// <summary>
        /// Detect vehicle from the given image
        /// </summary>
        /// <param name="image">The image</param>
        /// <returns>The detected vehicles.</returns>
        public Vehicle[] Detect(IInputArray image)
        {
            float vehicleConfidenceThreshold = 0.5f;
            float licensePlateConfidenceThreshold = 0.5f;

            int vehicleAttrSize = 72;
            double scale = 1.0;
            MCvScalar meanVal = new MCvScalar();

            List<Vehicle> vehicles = new List<Vehicle>();
            List<LicensePlate> plates = new List<LicensePlate>();
            using (InputArray iaImage = image.GetInputArray())
            using (Mat iaImageMat = iaImage.GetMat())
                foreach (DetectedObject vehicleOrPlate in _vehicleLicensePlateDetectionModel.Detect(image, 0.0f, 0.0f))
                {
                    Rectangle region = vehicleOrPlate.Region;

                    if (vehicleOrPlate.ClassId == 1 && vehicleOrPlate.Confident > vehicleConfidenceThreshold)
                    {
                        //this is a vehicle
                        Vehicle v = new Vehicle();
                        v.Region = region;

                        #region find out the type and color of the vehicle

                        using (Mat vehicle = new Mat(iaImageMat, region))
                        using (Mat vehicleBlob = DnnInvoke.BlobFromImage(
                            vehicle,
                            scale,
                            new Size(vehicleAttrSize, vehicleAttrSize),
                            meanVal,
                            false,
                            false,
                            DepthType.Cv32F))
                        {
                            _vehicleAttrRecognizer.SetInput(vehicleBlob, "input");

                            using (VectorOfMat vm = new VectorOfMat(2))
                            {
                                _vehicleAttrRecognizer.Forward(vm, new string[] { "color", "type" });
                                using (Mat vehicleColorMat = vm[0])
                                using (Mat vehicleTypeMat = vm[1])
                                {
                                    float[] vehicleColorData = vehicleColorMat.GetData(false) as float[];
                                    float maxProbColor = vehicleColorData.Max();
                                    int maxIdxColor = Array.IndexOf(vehicleColorData, maxProbColor);
                                    v.Color = _colorName[maxIdxColor];
                                    float[] vehicleTypeData = vehicleTypeMat.GetData(false) as float[];
                                    float maxProbType = vehicleTypeData.Max();
                                    int maxIdxType = Array.IndexOf(vehicleTypeData, maxProbType);
                                    v.Type = _vehicleType[maxIdxType];
                                }
                            }
                        }

                        #endregion

                        vehicles.Add(v);
                    }
                    else if (vehicleOrPlate.ClassId == 2 && vehicleOrPlate.Confident > licensePlateConfidenceThreshold)
                    {
                        //this is a license plate
                        LicensePlate p = new LicensePlate();
                        p.Region = region;

                        #region OCR on license plate
                        using (Mat plate = new Mat(iaImageMat, region))
                        {
                            using (Mat inputBlob = DnnInvoke.BlobFromImage(
                                plate,
                                scale,
                                new Size(94, 24),
                                meanVal,
                                false,
                                false,
                                DepthType.Cv32F))
                            {
                                _ocr.SetInput(inputBlob, "data");
                                using (Mat output = _ocr.Forward("decode"))
                                {
                                    float[] plateValue = output.GetData(false) as float[];
                                    StringBuilder licensePlateStringBuilder = new StringBuilder();
                                    foreach (int j in plateValue)
                                    {
                                        if (j >= 0)
                                        {
                                            licensePlateStringBuilder.Append(_plateText[j]);
                                        }
                                    }

                                    p.Text = licensePlateStringBuilder.ToString();
                                }
                            }
                        }
                        #endregion

                        plates.Add(p);
                    }
                }

            foreach (LicensePlate p in plates)
            {
                foreach (Vehicle v in vehicles)
                {
                    if (v.ContainsPlate(p))
                    {
                        v.LicensePlate = p;
                        break;
                    }
                }
            }

            return vehicles.ToArray();
        }

        /// <summary>
        /// Draw the vehicles to the image.
        /// </summary>
        /// <param name="image">The image to be drawn to.</param>
        /// <param name="vehicles">The vehicles.</param>
        public void Render(IInputOutputArray image, Vehicle[] vehicles)
        {
            foreach (Vehicle v in vehicles)
            {
                CvInvoke.Rectangle(image, v.Region, new MCvScalar(0, 0, 255), 2);
                String label = String.Format("{0} {1} {2}",
                    v.Color, v.Type, v.LicensePlate == null ? String.Empty : v.LicensePlate.Value.Text);
                CvInvoke.PutText(
                    image,
                    label,
                    new Point(v.Region.Location.X, v.Region.Location.Y + 20),
                    FontFace.HersheyComplex,
                    1.0,
                    new MCvScalar(0, 255, 0),
                    2);
            }
        }

        protected override void DisposeObject()
        {
            if (_vehicleLicensePlateDetectionModel != null)
            {
                _vehicleLicensePlateDetectionModel.Dispose();
                _vehicleLicensePlateDetectionModel = null;
            }

            if (_vehicleAttrRecognizer != null)
            {
                _vehicleAttrRecognizer.Dispose();
                _vehicleAttrRecognizer = null;
            }

            if (_ocr != null)
            {
                _ocr.Dispose();
                _ocr = null;
            }
        }
    }
}