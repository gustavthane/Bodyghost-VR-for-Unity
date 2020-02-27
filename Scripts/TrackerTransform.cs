using UnityEngine;
using HTC.UnityPlugin.Vive;
using HTC.UnityPlugin.VRModuleManagement;

/// <summary>
/// This script is unused, but serves has a way of repositioning the point clouds with <see cref="CameraPositioner"/>.
/// </summary>
public class TrackerTransform : MonoBehaviour {

	[SerializeField]
	private Transform transformSource;
	public string trackerSerialNumber;

	void Update() {
		if (false && VRModule.TryGetConnectedDeviceIndex(trackerSerialNumber, out uint deviceIndex)) {
			transform.position = VivePose.GetPose(deviceIndex).pos;
			transform.rotation = VivePose.GetPose(deviceIndex).rot;
		} else {
			transform.position = transformSource.position;
			transform.rotation = transformSource.rotation;
		}
	}
}
