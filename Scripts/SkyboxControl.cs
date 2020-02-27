using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Video;

/// <summary>
/// The <see cref="VideoPlayer"/> may cause delay for the point cloud syncronization, depending on how powerful your computer is.
/// </summary>
public class SkyboxControl : MonoBehaviour {
	private SkyboxControl self;

	/// <summary>
	/// Accessor name for the rotation value of a Panoramic <see cref="Skybox"/>'s <see cref="Material.shader"/>.
	/// </summary>
	public const string _Rotation = "_Rotation";

	void Awake() {
		self = this;

		videoPlayer = GetComponent<VideoPlayer>() ? GetComponent<VideoPlayer>() : self.gameObject.AddComponent<VideoPlayer>(); {
			videoPlayer.playOnAwake = true;
			videoPlayer.waitForFirstFrame = true;
			videoPlayer.isLooping = true;
			videoPlayer.skipOnDrop = true;

			videoPlayer.renderMode = VideoRenderMode.RenderTexture;
			videoPlayer.aspectRatio = VideoAspectRatio.Stretch;
			videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
		};

		skyboxMaterial = Instantiate(skyboxMaterial);
		skyboxRenderTexture = Instantiate(skyboxRenderTexture);
		GetComponent<MeshRenderer>().material = skyboxMaterial;

		skyboxMeshFilter = GetComponent<MeshFilter>();
		if (skyboxMeshFilter) {
			videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
			videoPlayer.targetMaterialRenderer = skyboxMeshFilter.GetComponent<MeshRenderer>();
		}
	}

	[SerializeField] private Material skyboxMaterial;
	[SerializeField] private RenderTexture skyboxRenderTexture;
	public Material SkyboxMaterial { get { return skyboxMaterial; } }

	private VideoPlayer videoPlayer;

	[SerializeField]
	private int maxNumRecursions = 3;

	private MeshFilter skyboxMeshFilter;
	
	public delegate void LoadSkybox_Handler(MonoBehaviour source, string path);
	public static LoadSkybox_Handler LoadSkybox_Event;

	public delegate void SkyboxRotation_Handler(MonoBehaviour source, float rotationDegrees);
	public static SkyboxRotation_Handler SkyboxRotation_Event;

	void OnEnable() {
		LoadSkybox_Event += LoadSkyboxFromFolderTree_Listener;
		SkyboxRotation_Event += SkyboxRotation_Listener;
	}
	void OnDisable() {
		LoadSkybox_Event -= LoadSkyboxFromFolderTree_Listener;
		SkyboxRotation_Event -= SkyboxRotation_Listener;
	}

	#region [REGION] This code is (hopefully) just a temporary hack.
	[SerializeField]
	/// <summary>
	/// This is pretty much only a quick hack to allow us to replace the normal skybox with a much cooler 3D model.
	/// <para/>
	/// In the Future(TM), it would be nice if we could load 3D models into the scene during runtime without the models first having to be included in Unity.
	/// </summary>
	private GameObject _3DModel;

	/// <summary>
	/// Show the much cooler 3D model instead of the normal skybox.
	/// </summary>
	private void Show3DModel() {
		if (!_3DModel) { return; }
		print("Showing");
		self.GetComponent<MeshRenderer>().enabled = false;
		self.videoPlayer.enabled = false;
		_3DModel.SetActive(true);
	}
	/// <summary>
	/// Hide the much cooler 3D model.
	/// </summary>
	private void Hide3DModel() {
		if (!_3DModel) { return; }
		print("Hiding");
		self.GetComponent<MeshRenderer>().enabled = true;
		self.videoPlayer.enabled = true;
		_3DModel.SetActive(false);
	}
	#endregion

	private void SetSkybox(string videoURL) {
		Hide3DModel();

		videoPlayer.url = videoURL;
		videoPlayer.targetTexture = skyboxRenderTexture;
		skyboxMaterial.mainTexture = skyboxRenderTexture;
		if (!skyboxMeshFilter) {
			RenderSettings.skybox = skyboxMaterial;
		}
	}

	[SerializeField]
	private string[] supportedSkyboxFileTypes = {
		".mp4", // ".jpg", ".jpeg", //...
	};
	private string[] GetFiles(string path) {
		List<string> files = new List<string>();
		foreach (string file in Directory.GetFiles(path)) {
			for (int i = 0; i < supportedSkyboxFileTypes.Length; i++) {
				if (file.EndsWith(supportedSkyboxFileTypes[i], StringComparison.OrdinalIgnoreCase)) {
					files.Add(file);
				}
			}
		}
		return files.ToArray();
	}

	/// <summary>
	/// Only video files are currently supported.
	/// </summary>
	private void RecursiveSkyboxLoader(string folder) {
		DirectoryInfo dir = null;
		if (Directory.Exists(folder)) {

			print("Searching recursively for a skybox video/image file:");

			dir = new DirectoryInfo(folder);

			for (int numRecursions = 0; numRecursions < maxNumRecursions; numRecursions++) {
				string[] videoURLs = GetFiles(dir.FullName);

				if (videoURLs.Length > 0) {
					print("   [" + numRecursions + "] file found: " + videoURLs[0]);

					SetSkybox(videoURLs[0]);
					return;
				}
				else if (dir.Parent != null) {
					print("   [" + numRecursions + "] folder: " + dir.FullName);

					// Go up to the parent directory and try again.
					dir = dir.Parent;
					continue;
				}
				else {
					print("   [" + numRecursions + "] found nothing in: " + dir.FullName);
					// Exit the loop if there is no parent directory to parse.
					break;
				}
			}
			// Displays the 3D Model (but only if the for loop exits without finding a skybox video/image.
			Show3DModel();
		}
	}

	private void LoadSkyboxFromFolderTree_Listener(MonoBehaviour source, string folder) {
		print("Attempting to load a skybox video from [" + folder + "] or its parents.");
		RecursiveSkyboxLoader(folder);
	}

	private void SkyboxRotation_Listener(MonoBehaviour source, float rotationDegrees) {
		print("Skybox before: " + skyboxMaterial.GetFloat(SkyboxControl._Rotation));
		skyboxMaterial.SetFloat(SkyboxControl._Rotation, rotationDegrees);
		print("skybox after: " + skyboxMaterial.GetFloat(SkyboxControl._Rotation));
	}
}
