using System;
using System.Globalization;
using LitJson;
using Robot;
using Unity.XR.PICO.TOBSupport;
using Unity.XR.PXR;
using UnityEngine;

public partial class UICameraCtrl
{
    private void OnNetReceive(string functionName, string value)
    {
        // Haptic feedback is independent of the camera UI visibility.
        if (functionName == "Haptic")
        {
            HandleHapticCommand(value);
            return;
        }

        if (!gameObject.activeSelf)
        {
            return;
        }

        if (functionName == "CameraRecord")
        {
            bool record = false;

            int videoWidth = 1024;
            int videoHeight = 768 / 2;
            int videoFps = 30;
            int bitrate = 10 * 1024 * 1024;
            int captureRenderMode = (int)PXRCaptureRenderMode.PXRCapture_RenderMode_3D;

            try
            {
                Debug.Log("value:" + value);
                value = value.Replace("\\", "");
                JsonData json = JsonMapper.ToObject(value);
                int.TryParse(json["on"].ToString(), out var on);
                record = on == 1;
                if (record)
                {
                    int.TryParse(json["videoFps"].ToString(), out videoFps);
                    int.TryParse(json["videoWidth"].ToString(), out videoWidth);
                    int.TryParse(json["videoHeight"].ToString(), out videoHeight);
                    int.TryParse(json["bitrate"].ToString(), out bitrate);
                    if (json.ContainsKey("captureRenderMode"))
                    {
                        int.TryParse(json["captureRenderMode"].ToString(), out captureRenderMode);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }


            if (RecordBtn.On != record)
            {
                if (record)
                {
                    StartRecord(videoWidth, videoHeight, videoFps, bitrate, false);
                }
                else
                {
                    StopRecord();
                }
            }
        }
        else if (functionName == "GetCameraParam")
        {
            Debug.Log("value:" + value);
            value = value.Replace("\\", "");
            JsonData json = JsonMapper.ToObject(value);
            int width = 2048;
            int height = 1536;
            if (json.ContainsKey("width"))
            {
                width = int.Parse(json["width"].ToString());
            }

            if (json.ContainsKey("height"))
            {
                height = int.Parse(json["height"].ToString());
            }

            string cameraExtrinsics = Utils.GetCameraExtrinsicsStrE();
            string cameraIntrinsics = Utils.GetCameraIntrinsicsStrE(width, height);
            JsonData cameraParam = new JsonData();
            cameraParam["resolution"] = string.Format("{0}x{1}", width, height);
            cameraParam["cameraExtrinsics"] = cameraExtrinsics;
            cameraParam["cameraIntrinsics"] = cameraIntrinsics;
            TcpHandler.SendFunctionValue("GetCameraParam", cameraParam.ToJson());
        }
        else if (functionName == "ResponsePcCamera")
        {
            Debug.Log("value:" + value);
            value = value.Replace("\\", "");
            JsonData json = JsonMapper.ToObject(value);

            int.TryParse(json["res"].ToString(), out var res);

            if (res == 0)
            {
                JsonData cameraList = json["CameraList"];
                CameraRequestDialog.SetCameraList(cameraList);

                CameraRequestDialog.Show(
                    () =>
                    {
                        Debug.Log(CameraRequestDialog.ResolutionWidth + " " + CameraRequestDialog.ResolutionHeight +
                                  " " +
                                  CameraRequestDialog.Fps + " " + CameraRequestDialog.Bitrate);

                        int port = Utils.GetAvailablePort();
                        RemoteCameraWindowObj.SetActive(true);
                        RemoteCameraWindowObj.GetComponent<RemoteCameraWindow>()
                            .StartListen(CameraRequestDialog.ResolutionWidth, CameraRequestDialog.ResolutionHeight,
                                CameraRequestDialog.Fps, CameraRequestDialog.Bitrate, port);
                    },
                    null);
            }
            else
            {
                Debug.LogWarning("ResponsePcCamera fail res+" + res);
            }
        }
        else if (functionName == "RequestVRCamera")
        {
            Toast.Show("Receive VR camera Request");
            //The VR camera image acquisition is only effective on the B-end device of Pico4U.
            if (!Utils.IsPico4U())
            {
                Debug.LogWarning("Please use B-end pico4U devices and apply for camera permissions.");
                return;
            }

            value = value.Replace("\\", "");
            JsonData json = JsonMapper.ToObject(value);

            int.TryParse(json["on"].ToString(), out var on);
            bool open = on == 1;
            if (open)
            {
                //  string ip = json["ip"].ToString();
                int.TryParse(json["port"].ToString(), out var port);
                int.TryParse(json["width"].ToString(), out var width);
                int.TryParse(json["height"].ToString(), out var height);
                int.TryParse(json["fps"].ToString(), out var fps);
                int.TryParse(json["bitrate"].ToString(), out var bitrate);
                int.TryParse(json["captureRenderMode"].ToString(), out var captureRenderMode);


                string ip = TcpHandler.GetTargetIP;
                _recordTrackingData = false;
                CameraHandle.StartCameraPreview(width, height, fps, bitrate, 0, captureRenderMode,
                    () => { CameraHandle.StartSendImage(ip, port); });
            }
            else
            {
                StopSendImage();
                JsonData cameraParam = new JsonData();
                TcpHandler.SendFunctionValue("VRCameraClose", cameraParam.ToJson());
            }
        }
    }

    /// <summary>
    /// Handles a Python-originated haptic command.
    /// Expected value JSON:
    /// {"hand":"right","amplitude":0.5,"duration_ms":100,"frequency":150}
    /// </summary>
    private void HandleHapticCommand(string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Debug.LogWarning("[Haptic] Ignoring an empty command.");
                return;
            }

            JsonData json = JsonMapper.ToObject(value);
            if (json == null || !json.IsObject ||
                !json.ContainsKey("hand") ||
                !json.ContainsKey("amplitude") ||
                !json.ContainsKey("duration_ms"))
            {
                Debug.LogWarning("[Haptic] Invalid command. Required fields: hand, amplitude, duration_ms.");
                return;
            }

            string hand = json["hand"].ToString().Trim().ToLowerInvariant();

            if (!float.TryParse(
                    json["amplitude"].ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float amplitude))
            {
                Debug.LogWarning("[Haptic] Invalid amplitude.");
                return;
            }

            if (!int.TryParse(
                    json["duration_ms"].ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int durationMs))
            {
                Debug.LogWarning("[Haptic] Invalid duration_ms.");
                return;
            }

            int frequency = 150;
            if (json.ContainsKey("frequency") &&
                !int.TryParse(
                    json["frequency"].ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out frequency))
            {
                Debug.LogWarning("[Haptic] Invalid frequency.");
                return;
            }

            PXR_Input.VibrateType vibrateType;
            switch (hand)
            {
                case "left":
                    vibrateType = PXR_Input.VibrateType.LeftController;
                    break;
                case "right":
                    vibrateType = PXR_Input.VibrateType.RightController;
                    break;
                case "both":
                    vibrateType = PXR_Input.VibrateType.BothController;
                    break;
                default:
                    Debug.LogWarning("[Haptic] Unsupported hand: " + hand);
                    return;
            }

            amplitude = Mathf.Clamp01(amplitude);
            durationMs = Mathf.Clamp(durationMs, 0, 65535);
            frequency = Mathf.Clamp(frequency, 50, 500);

            PXR_Input.SendHapticImpulse(
                vibrateType,
                amplitude,
                durationMs,
                frequency
            );

            Debug.Log(
                $"[Haptic] Triggered {hand} controller: " +
                $"amplitude={amplitude:0.##}, duration_ms={durationMs}, frequency={frequency}Hz"
            );
        }
        catch (Exception e)
        {
            Debug.LogError("[Haptic] Failed to process command: " + e);
        }
    }
}
}