using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using HTC.UnityPlugin.Vive;

public class CameraPositioner : MonoBehaviour {

	private ViveRoleProperty calibrationController = ViveRoleProperty.New(HandRole.RightHand);
	private Pose calibrationControllerReferencePose;

	private Pose[] pointCloudReferencePoses;

	private float skyboxReferenceRotation;

	public Bodyghost bodyghost;

	private TrackerTransform[] poseTransforms;
	private int currentAdjustedTransformIndex = 0;
	private Transform[] poseTransformOriginMarkers;
    private float adjustmentRatio = 1.0f;

	private List<GameObject> markers;

	private const string calibrationFileName = "cameraPositions.json";

	private float[] originalPointCloudPointSizes;

	[SerializeField]
	private SkyboxControl skyboxControl;

	private enum Mode {
		NONE,
		ADJUST_CAMERA_ROTATION,
		ADJUST_CAMERA_POSITION,
		SET_CAMERA_TRANSFORM,
		ADJUST_POINTCLOUD
	};
	Mode mode = Mode.NONE;

	private enum AdjustmentTarget {
		SINGLE_POINTCLOUD,
		ALL_POINTCLOUDS,
		SKYBOX
	}
	private AdjustmentTarget currentAdjustmentTarget = AdjustmentTarget.SINGLE_POINTCLOUD;
	
	// Use this for initialization
	void Start() {
		markers = new List<GameObject>();

		poseTransforms = bodyghost.GetComponentsInChildren<TrackerTransform>(true);
		pointCloudReferencePoses = new Pose[poseTransforms.Length];
		poseTransformOriginMarkers = CreateOriginMarkers();

		originalPointCloudPointSizes = new float[poseTransforms.Length];
	}

	void Update() {
		for (int i = 0; i < 16; i++) {
			DeviceRole role = (DeviceRole) i;
			if (ViveInput.GetPressDownEx<DeviceRole>(role, ControllerButton.System)) {
				Debug.Log("System button on deviceRole: " + role);
			}

			if (ViveInput.GetPressDownEx<DeviceRole>(role, ControllerButton.Trigger)) {
				Debug.Log("Trigger button on deviceRole: " + role);
			}

			if (ViveInput.GetPressDownEx<DeviceRole>(role, ControllerButton.Menu)) {
				Debug.Log("Menu button on deviceRole: " + role);
			}
		}
		
		if (ViveInput.GetPressDown(calibrationController, ControllerButton.Trigger)) {
			Debug.Log("Trigger pressed");

			switch (mode) {
				case Mode.ADJUST_POINTCLOUD: {
						SetAdjustmentReferencePoses();
					}
					break;
			}
		}
		
		if (ViveInput.GetPress(calibrationController, ControllerButton.Trigger)) {
			switch (mode) {
				case Mode.ADJUST_POINTCLOUD: {
						switch (currentAdjustmentTarget) {
							case AdjustmentTarget.SINGLE_POINTCLOUD: {
									AdjustPointcloud(poseTransforms[currentAdjustedTransformIndex], pointCloudReferencePoses[currentAdjustedTransformIndex]);
								}
								break;
							case AdjustmentTarget.ALL_POINTCLOUDS: {
									for (int i = 0; i < poseTransforms.Length; i++) {
										AdjustPointcloud(poseTransforms[i], pointCloudReferencePoses[i]);
									}
								}
								break;
							case AdjustmentTarget.SKYBOX: {
									AdjustSkybox();
								}
								break;
						}
					}
					break;
			}
		}
		
		Vector2 scroll = ViveInput.GetScrollDelta(HandRole.RightHand, ScrollType.Trackpad, Vector2.up);
        if (Mathf.Abs(scroll.y) > 0.01f) {
            adjustmentRatio -= scroll.y * 0.01f;
            adjustmentRatio = Mathf.Clamp(adjustmentRatio, 0.05f, 2.0f);
        }

        if (ViveInput.GetPressDown(calibrationController, ControllerButton.Pad)) {
			Debug.Log("Pad pressed");

			switch (mode) {
				case Mode.ADJUST_POINTCLOUD:

					switch (currentAdjustmentTarget) {
						case AdjustmentTarget.SINGLE_POINTCLOUD:
							currentAdjustedTransformIndex++;
							if (currentAdjustedTransformIndex >= poseTransforms.Length) {
								currentAdjustmentTarget = AdjustmentTarget.ALL_POINTCLOUDS;
							}
							currentAdjustedTransformIndex %= poseTransforms.Length;

							break;
						case AdjustmentTarget.ALL_POINTCLOUDS: 
							currentAdjustmentTarget = AdjustmentTarget.SKYBOX;
							break;
						case AdjustmentTarget.SKYBOX:
							currentAdjustmentTarget = AdjustmentTarget.SINGLE_POINTCLOUD;
							break;
					}
					
					Material material = poseTransforms[currentAdjustedTransformIndex].GetComponentInChildren<RsPointCloudRenderer>().GetComponent<MeshRenderer>().material;

					for (int i = 0; i < originalPointCloudPointSizes.Length; i++) {
						originalPointCloudPointSizes[i] = material.GetFloat("_PointSize");
					}
					material.SetFloat("_PointSize", originalPointCloudPointSizes[currentAdjustedTransformIndex] * 0.5f);

                    for (int i = 0; i < poseTransforms.Length; i++) {
                        print("Indices: " + i + " " + currentAdjustedTransformIndex);
                        if (i != currentAdjustedTransformIndex) {
                            Material otherMaterial = poseTransforms[i].GetComponentInChildren<RsPointCloudRenderer>().GetComponent<MeshRenderer>().material;
                            otherMaterial.SetFloat("_PointSize", originalPointCloudPointSizes[i]);
                        }
                    }
					break;
			}
			Debug.Log("camera control mode: " + mode + ": " + currentAdjustmentTarget);
		}

		if (ViveInput.GetPressDown(calibrationController, ControllerButton.Menu)) {
			Debug.Log("Menu pressed");
			switch (mode) {
				case Mode.NONE: {
						mode = Mode.ADJUST_POINTCLOUD;
						EnableOriginMarkers(true);
					}
					break;
				case Mode.ADJUST_POINTCLOUD: {
						mode = Mode.NONE;
						EnableOriginMarkers(false);
					}
					break;
			}
		}
	}

	/// <summary>
	/// Throws an error of <see cref="currentPoseTransform"/> is not assigned.
	/// </summary>
	private void SetAdjustmentReferencePoses() {
		calibrationControllerReferencePose = VivePose.GetPose(calibrationController, null);
		for (int i = 0; i < poseTransforms.Length; i++) {
			pointCloudReferencePoses[i].position = poseTransforms[i].transform.position;
			pointCloudReferencePoses[i].rotation = poseTransforms[i].transform.rotation;
		}

		if (skyboxControl) {
			skyboxReferenceRotation = skyboxControl.SkyboxMaterial.GetFloat(SkyboxControl._Rotation);
		}
		if (RenderSettings.skybox) {
			skyboxReferenceRotation = RenderSettings.skybox.GetFloat(SkyboxControl._Rotation);
		}
	}

	Transform[] CreateOriginMarkers() {
		// Perhaps we should insert deletion of old markers here...

		List<Transform> originMarkers = new List<Transform>();
		foreach (TrackerTransform poseTransform in poseTransforms) {
			GameObject originMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			originMarker.transform.SetParent(poseTransform.transform);
			originMarker.transform.localPosition = Vector3.zero;
			originMarker.transform.localRotation = Quaternion.identity;
			originMarker.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
			originMarker.GetComponent<MeshRenderer>().materials[0].color = Color.magenta;
			originMarkers.Add(originMarker.transform);
			originMarker.SetActive(false);
		}
		return originMarkers.ToArray();
	}

	private void EnableOriginMarkers(bool enable = true) {
		foreach (Transform trn in poseTransformOriginMarkers) {
			trn.gameObject.SetActive(enable);
		}
	}
	
	private void AdjustSkybox() {
		Pose controllerPose = VivePose.GetPose(calibrationController);
		// The delta rotation of the hand controller
		Quaternion rotationOffset = controllerPose.rotation * Quaternion.Inverse(calibrationControllerReferencePose.rotation);
		print("rotationOffset: w=" + rotationOffset.w + " x=" + rotationOffset.x + " y=" + rotationOffset.y + " z=" + rotationOffset.z);

		// Force unity to use "small" angles (below 180 degrees)
		if (rotationOffset.w < 0f) {
			rotationOffset.w = -rotationOffset.w;
			rotationOffset.x = -rotationOffset.x;
			rotationOffset.y = -rotationOffset.y;
			rotationOffset.z = -rotationOffset.z;
		}
		rotationOffset.ToAngleAxis(out float deltaRotationAngle, out Vector3 deltaRotationAxis);
		// Force Unity to not switch the axis after 180 degrees rotation. We simply set current rotation as reference
		if (deltaRotationAngle > 150) {
			print("Resetting offset angle.");
			SetAdjustmentReferencePoses();
			return;
		}

		print("rotationOffset angle: " + deltaRotationAngle + "\t axis: " + deltaRotationAxis);
		
		Quaternion scaledRotationOffset = Quaternion.Slerp(Quaternion.identity, rotationOffset, adjustmentRatio);
		Vector3 scaledRotationOffsetEuler = scaledRotationOffset.eulerAngles;

		print("skyboxReferenceRotation: " + skyboxReferenceRotation);

		float newRotation = skyboxReferenceRotation + scaledRotationOffsetEuler.y;
		newRotation %= 360;
		if (RenderSettings.skybox) {
			RenderSettings.skybox.SetFloat(SkyboxControl._Rotation, newRotation);
		}
		else {
			SkyboxControl.SkyboxRotation_Event.Invoke(this, newRotation);
		}
	}

	private void AdjustPointcloud(TrackerTransform targetTransform, Pose refPose) {

		Pose controllerPose = VivePose.GetPose(calibrationController);
		// The delta rotation of the hand controller
		Quaternion rotationOffset = controllerPose.rotation * Quaternion.Inverse(calibrationControllerReferencePose.rotation);
        print("rotationOffset: w=" + rotationOffset.w + " x=" + rotationOffset.x + " y=" + rotationOffset.y + " z=" + rotationOffset.z);
        // Force Unity to use "small" angles (below 180 degrees)
        if (rotationOffset.w < 0f) {
            rotationOffset.w = -rotationOffset.w;
            rotationOffset.x = -rotationOffset.x;
            rotationOffset.y = -rotationOffset.y;
            rotationOffset.z = -rotationOffset.z;
        }
        rotationOffset.ToAngleAxis(out float deltaRotationAngle, out Vector3 deltaRotationAxis);
        // Force Unity to not switch the axis after 180 degrees rotation. We simply set current rotation as reference
        if (deltaRotationAngle > 150) {
            SetAdjustmentReferencePoses();
            return;
        }
        print("rotationOffset angle: " + deltaRotationAngle + "\t axis: " + deltaRotationAxis);
        Vector3 positionOffset = controllerPose.position - calibrationControllerReferencePose.position;


		// Always rotate and position from the reference
		targetTransform.transform.rotation = refPose.rotation;
		targetTransform.transform.position = refPose.position;

		Quaternion scaledRotationOffset = Quaternion.Slerp(Quaternion.identity, rotationOffset, adjustmentRatio);
		print("scaledRotationOffset: " + scaledRotationOffset);
		scaledRotationOffset.ToAngleAxis(out float scaledDeltaRotationAngle, out Vector3 scaledDeltaRotationAxis);
		print("scaledRotationOffset angle: " + scaledDeltaRotationAngle + "\t axis: " + scaledDeltaRotationAxis);


		targetTransform.transform.RotateAround(calibrationControllerReferencePose.position, scaledDeltaRotationAxis, scaledDeltaRotationAngle);
		targetTransform.transform.position += (positionOffset * adjustmentRatio);

    }

	private void CreateMarker() {
		GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		sphere.transform.localScale = new Vector3(0.04f, 0.04f, 0.04f);
		MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();
		renderer.materials[0].color = Color.magenta;

		sphere.transform.parent = (this.gameObject.transform);

		markers.Add(sphere);

		ViveInput.TriggerHapticPulse(calibrationController, 500);
	}

	private void SaveCalibrationData() {
		CameraCalibrationData saveData = new CameraCalibrationData();

		string dataAsJson = JsonUtility.ToJson(saveData);

		string filePath = Application.dataPath + "/" + calibrationFileName;
		File.WriteAllText(filePath, dataAsJson);
	}
	
	private void LoadCalibrationData() {
		string filePath = Application.dataPath + "/" + calibrationFileName;

		if (File.Exists(filePath)) {
			string dataAsJson = File.ReadAllText(filePath);
			CameraCalibrationData loadedData = JsonUtility.FromJson<CameraCalibrationData>(dataAsJson);
		}
	}
}

[Serializable]
class CameraCalibrationData {
	public float fieldOfView;
	public Pose cameraPose;
}
