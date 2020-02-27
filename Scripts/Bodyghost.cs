using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using Intel.RealSense;
using NAudio.Wave;
using HTC.UnityPlugin.Vive;


public class Bodyghost : MonoBehaviour {

	[SerializeField]
	public List<DevContainer> devContainers = new List<DevContainer>();

	[SerializeField]
	private RsProcessingBlock[][] allDaBlockses = new RsProcessingBlock[0][];

	private bool fileBrowserIsDisplayed = false;

	[SerializeField]
	private MetaData metaData = new MetaData();
	private MetaData sessionCalibration = new MetaData();

	private bool fetchNewFrameSet = false;

	private enum SyncState {
		MASTER_IS_WAITING,
		ALL_PLAYERS_ARE_CAUGHT_UP,
		PLAYING
	}
	private SyncState currentSyncState = SyncState.MASTER_IS_WAITING;

	private enum SyncMethod {
		EVERY_FRAME_TO_REFERENCE,
		SEEK_TO_TIMESTAMP,
		NO_SYNC
	}
	private SyncMethod syncMethod = SyncMethod.EVERY_FRAME_TO_REFERENCE;

	private Context ctx;

	private string targetDirectory;


	private void Awake() {
		SetupAudioWaveformSyncTexture();

		ctx = new Context();
		ctx.OnDevicesChanged += DevicesChanged;

		DeviceList devices = ctx.QueryDevices();
		print("There are " + devices.Count + " connected RealSense devices.");

		RsProcessingPipe[] pipes = GetComponentsInChildren<RsProcessingPipe>();
		allDaBlockses = new RsProcessingBlock[pipes.Length][];
		for (int i = 0; i < pipes.Length; i++) {
			RsProcessingBlock[] someProcessingBlocks = pipes[i].profile._processingBlocks.ToArray();
			allDaBlockses[i] = someProcessingBlocks;
		}

		playlistContainer = MetaDataUtility.LoadPlaylists();
	}

	void Start() {
		print("Start: " + playlistContainer);
		if (playlistContainer != null) {
			int selectedPlaylist = playlistContainer.selectedPlaylist;
			if (0 <= selectedPlaylist && selectedPlaylist < playlistContainer.playlists.Count) {
				print("Automatically starting the last played playlist. Playlist[" + selectedPlaylist + "].");
				StartPlaybackSequence(playlistContainer.playlists[selectedPlaylist]);
			}
		}
		fileBrowserIsDisplayed = SimpleFileBrowser.FileBrowser.IsOpen;
		SimpleFileBrowser.FileBrowser.SetFilters(false, ".bag");
	}

	private void DevicesChanged(DeviceList removed, DeviceList added) {
		print("---> devices changed callback");
		print("removed " + removed.Count + " device(s)");
		print("added " + added.Count + " device(s)");

		foreach (Device d in added) {
			print(d.Info[CameraInfo.SerialNumber]);
		}

		foreach (Device d in removed) {
			print(d.Info[CameraInfo.SerialNumber]);
		}
	}


	private WaveOutEvent audioOutputDevice;
	private AudioFileReader audioFile;
	private bool audioPlaybakActive = true;
	private bool audioSynced = false;
	private Texture2D audioWaveformTexture;
	private Texture2D audioWaveformOverlayTexture;

	private void SetupAudioWaveformSyncTexture() {
		audioWaveformTexture = new Texture2D(10000, 150);
		audioWaveformOverlayTexture = new Texture2D(audioWaveformTexture.width / 10, audioWaveformTexture.height);

		for (int y = 0; y < audioWaveformTexture.height; y++) {
			for (int x = 0; x < audioWaveformTexture.width; x++) {
				audioWaveformTexture.SetPixel(x, y, Color.Lerp(Color.blue, Color.clear, 0.5f));
			}
		}
		audioWaveformTexture.Apply();

		// Create "playback location pointer" for the waveform texture.
		for (int y = 0; y < audioWaveformOverlayTexture.height; y++) {
			for (int x = 0; x < audioWaveformOverlayTexture.width; x++) {
				if (x < audioWaveformOverlayTexture.width / 2 + 2 && x > audioWaveformOverlayTexture.width / 2 - 2) {
					audioWaveformOverlayTexture.SetPixel(x, y, Color.Lerp(Color.black, Color.clear, 0.5f));
				} else {
					audioWaveformOverlayTexture.SetPixel(x, y, Color.clear);
				}
			}
		}
		audioWaveformOverlayTexture.Apply();
	}

	[System.Obsolete("Not yet implemented")]
	private class AudioVariables {
		public WaveOutEvent audioOutputDevice;
		public AudioFileReader audioFile;
		public bool audioPlaybakActive = true;
		public bool audioSynced = false;
		public Texture2D audioWaveformTexture;
		public Texture2D audioWaveformOverlayTexture;

		public long audioPosWhenClicked;
		public long audioSliderPos = 0;
		public bool audioSliderPressed = false;
		public long audioSyncMark = 0;
		public ulong pointcloudSyncMark = 0;

		public bool mousePressedInsideAudioTexture = false;
		public double mousePressedOffsetPosition = 0d;
	}
	[System.Obsolete("Not yet implemented")]
	private readonly AudioVariables audioVars = new AudioVariables();

	private long audioPosWhenClicked;
	private long audioSliderPos = 0;
	private bool audioSliderPressed = false;
	private long audioSyncMark = 0;
	private ulong pointcloudSyncMark = 0;

	private bool mousePressedInsideAudioTexture = false;
	private double mousePressedOffsetPosition = 0d;

	private bool showDeveloperControls = false;
	private bool showPlaylistGUI = false;

	[SerializeField]
	private SkyboxControl skyboxControl;

	private void OnGUI() {
		if (fileBrowserIsDisplayed) {
			return;
		}
		float edgeOffset = 20f;
		float btnWidth = 136f;
		float btnHeight = 24f;

		Rect layoutRect = new Rect(50, 50, Screen.width - 100, Screen.height - 100);
		//var guiScale = new Vector3(Screen.height / 600.0f, Screen.height / 600.0f, 1.0f);
		//GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, guiScale);
		
		Rect guiToggleButtonRect = new Rect(
			Screen.width - btnWidth - edgeOffset, 
			Screen.height - btnHeight - edgeOffset - 30, 
			btnWidth, btnHeight
		);

		if (GUI.Button(guiToggleButtonRect, (showPlaylistGUI ? "Hide" : "Show") + " Playlist GUI")) {
			showPlaylistGUI = !showPlaylistGUI;
		}
		guiToggleButtonRect = new Rect(
			Screen.width - btnWidth - edgeOffset,
			Screen.height - btnHeight - edgeOffset,
			btnWidth, btnHeight
		);
		if (GUI.Button(guiToggleButtonRect, (showDeveloperControls ? "Hide" : "Show") + " Developer GUI")) {
			showDeveloperControls = !showDeveloperControls;
		}

		GUILayout.BeginArea(layoutRect);
		{
			if (showDeveloperControls) {
				DevGUI(layoutRect);
			}
			if (showPlaylistGUI) {
				GUI_PlaylistInteractions();
			}
		}
		GUILayout.EndArea();

		if (GUI.changed) {
			DisableOrbitCameraControl();
		}
	}

	#region [REGION] Logic from GUI interactions
	/// <summary>
	/// Freeze the <see cref="OrbitCameraControl"/> while interacting with the GUI.
	/// </summary>
	private void DisableOrbitCameraControl() {
		OrbitCameraControl orbitCam = Camera.main.GetComponent<OrbitCameraControl>();
		if (orbitCam) {
			orbitCam._moveSpeedFactor = 0;
			orbitCam._rotateSpeedFactor = 0;
			orbitCam._rotateSpeedFactor = 0;
		}
	}

	private void DevGUI(Rect layoutRect) {
		GUILayout.BeginVertical();
		{
			if (audioFile != null) {
				GUILayout.Space(128);
				//double playPos = (double)audioFile.Position / (double)audioFile.Length;
				double audioPlayPos = audioFile.CurrentTime.TotalMilliseconds / audioFile.TotalTime.TotalMilliseconds;

				//print("playPos: " + playPos);
				Rect audioRect = new Rect(0, 0, Screen.width - layoutRect.x * 2, 128);
				//GUI.DrawTextureWithTexCoords(audioRect, audioWaveformTexture, new Rect((float) audioPlayPos, 0f, .1f, 1.0f), true);
				//audioSliderPos = (long) audioPlayPos;
				audioSliderPos = (long)GUILayout.HorizontalSlider(audioSliderPos, 0, audioFile.Length);

				if (GUI.changed && Input.GetMouseButton(0) && !audioSliderPressed) {
					audioSliderPressed = true;
					audioOutputDevice.Play();
					print("sliderpressed set to true");
				}
				else if (Input.GetMouseButtonUp(0) && audioSliderPressed) {
					if (!audioPlaybakActive) {
						audioOutputDevice.Pause();
					}
					audioSliderPressed = false;
				}
				if (audioSliderPressed) {
					audioFile.Position = audioSliderPos;
				}

				int mouseX = (int)Input.mousePosition.x;
				int mouseY = Screen.height - (int)Input.mousePosition.y;
				//float xPos = GetAudioTexturePosition(layoutRect);
				int mouseTextureX = mouseX - (int)layoutRect.x - (int)audioRect.x;
				float mouseTextureXNormalized = (float)mouseTextureX / audioRect.width;

				Rect clickableAudioRect = new Rect(
					layoutRect.x - audioRect.x,
					layoutRect.y - audioRect.y,
					audioRect.width + layoutRect.x,
					audioRect.height + layoutRect.y
				);
				//GUILayout.Label("clickableAudioRect: " + clickableAudioRect);

				if (Input.GetMouseButtonDown(0) && IsPointInsideArea(mouseX, mouseY, clickableAudioRect)) { //xInsideArea && yInsideArea) {
					audioOutputDevice.Play();
					audioPosWhenClicked = audioFile.Position;
					mousePressedInsideAudioTexture = true;
					mousePressedOffsetPosition = mouseTextureXNormalized; // audioPlayPos;
				}
				if (Input.GetMouseButtonUp(0) && mousePressedInsideAudioTexture) {
					if (!audioPlaybakActive) {
						audioOutputDevice.Pause();
					}
					mousePressedInsideAudioTexture = false;
				}
				if (mousePressedInsideAudioTexture) {
					DisableOrbitCameraControl();

					// Update audioPlayPos or whatever in here.
					// ...
					//double mouseDeltaX = Input.mousePosition.x / audioRect.width;
					double mouseDeltaX = mousePressedOffsetPosition - mouseTextureXNormalized;
					//GUILayout.Label("Mouse Delta X: " + mouseDeltaX);
					//GUILayout.Label("Mouse Offset X: " + mousePressedOffsetPosition);
					//audioPlayPos = (mouseDeltaX - mousePressedOffsetPosition);
					audioFile.Position = audioPosWhenClicked + (long)(mouseDeltaX * 1000000f);

				}
				//cap the playpos within the possible values
				audioPlayPos = Math.Min(audioPlayPos, audioFile.TotalTime.TotalMilliseconds);
				audioPlayPos = Math.Max(audioPlayPos, 0d);
				//GUILayout.Label("audioPlayPos: " + audioPlayPos);

				GUI.DrawTextureWithTexCoords(audioRect, audioWaveformTexture, new Rect((float)audioPlayPos - (0.1f / 2), 0f, 0.1f, 1.0f), true);
				GUI.DrawTexture(audioRect, audioWaveformOverlayTexture);
			}

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Restart Master Player")) {
				RestartMasterPlayer();
			}
			//if (GUILayout.Button(((audioOutputDevice.PlaybackState == PlaybackState.Paused) ? "Play" : "Pause") + " Sound")) {
			if (GUILayout.Button("Play/Pause Sound")) {
				PlayOrPauseSound();
			}
			if (GUILayout.Button("Set audio & master sync marks")) {
				SetSyncMarks(audioFile.Position, devContainers[0].latestFrameSet.Number);
			}
			if (devContainers.Count > 0) {
				if (GUILayout.Button((GetPlaybackDevice(devContainers[0]).Realtime ? "Disable" : "Enable") + " Realtime")) {
					ToggleRealtimeMasterPlayer();
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			foreach (DevContainer devContainer in devContainers) {
				string devInfo = "";
				devInfo += devContainer.serialNumber + ": ";
				//devInfo += "latestStamp=" + devContainer.latestFrameStamp + "   ";
				//devInfo += "streamingStampOffset=" + devContainer.streamingStampOffset + "   ";
				//devInfo += "recordingStampOffset=" + devContainer.recordingStampOffset + "   ";
				if (devContainer.latestFrameSet != null)
					devInfo += "frameIndex=" + devContainer.latestFrameSet.Number + "   ";
				devInfo += "recordingFrameIndexOffset=" + devContainer.recordingFrameIndexOffsets[devContainer.currentRecordingFrameOffsetIndex].offset + "   ";
				devInfo += "skippedFrames=" + devContainer.skippedFramesLastPoll + "   ";
				GUILayout.Label(devInfo);
			}

			GUILayout.Label("FPS: " + (1.0 / Time.deltaTime));
			GUILayout.Space(5);
			GUILayout.BeginHorizontal();

			if (GUILayout.Button("Open Recording")) {
				string dialogTitle = "Select a .bag file to play";
				//string selectedFolder = OpenFolder(dialogTitle);
				//if (!string.IsNullOrEmpty(selectedFolder)) {
				//	StartPlaying(selectedFolder);
				//}
				SimpleFileBrowser.FileBrowser.OnCancel onCancel = () => {
					fileBrowserIsDisplayed = false;
				};
				SimpleFileBrowser.FileBrowser.OnSuccess onSuccess = (selectedFile) => {
					fileBrowserIsDisplayed = false;
					if (!string.IsNullOrEmpty(selectedFile) && File.Exists(selectedFile)) {
						StartPlaying(GetContainingFolder(selectedFile));
					}
				};
				SimpleFileBrowser.FileBrowser.ShowLoadDialog(onSuccess, onCancel, false, Application.dataPath, dialogTitle);
				fileBrowserIsDisplayed = true;
			}

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			#region [REGION] Old and unused code
			/*
			//GUILayout.Label(Path.Combine(new string[] { Application.dataPath, "Resources", "Skyboxes" }));
			try {
				string skyboxFolder = Directory.GetDirectories(Path.Combine(new string[] { Application.dataPath, "Resources", "Skyboxes" }), "Materials")[0];
				string[] skyboxes = Directory.GetFiles(skyboxFolder, "*.mat");
				//string[] skyboxes = Directory.GetFiles("Assetqs/Resources/Materials", "*.mat");

				selectedSkybox = GUILayout.SelectionGrid(selectedSkybox, skyboxes, 1);
				if (lastSelectedSkybox != selectedSkybox) {
					lastSelectedSkybox = selectedSkybox;
					//print(skyboxes[selectedSkybox]);

					string simplifiedSkyboxName = Path.GetFileNameWithoutExtension(skyboxes[selectedSkybox]);
					print(selectedSkybox + " Skybox name: " + simplifiedSkyboxName);
					//Material material = Resources.Load<Material>(Path.Combine("Skyboxes", "Materials", simplifiedSkyboxName));
					Material material = Resources.Load<Material>(simplifiedSkyboxName);

					print("Skybox material: " + material);
					//Texture skyboxTexture = material.GetTexture("_Tex");
					//print(skyboxTexture);
					skyboxRenderer.material = material;
					//skyboxRenderer.material.SetTexture("_Tex", skyboxTexture);
					//skyboxRenderer.material.mainTexture = material.mainTexture;

					//AssetBundle asset = AssetBundle.LoadFromFile(skyboxes[selectedSkybox],);
					//skyboxRenderer.material = asset;

					//print(selectedSkybox + " Skybox: " + skyboxes[selectedSkybox]);
					//Shader skyboxShader = Shader.Find("Skybox/Cubemap");
					//Material skyboxMaterial = new Material(skyboxShader);
					//Texture skyboxTexture = new Texture2D(2048, 2048);
					//skyboxRenderer.material = skyboxMaterial;
				}
			} catch (Exception e ) {
				Debug.LogError(e);
			}
			*/

			//GUILayout.Space(5);
			//GUILayout.Label("Skyboxes");
			//GUILayout.BeginHorizontal();
			//string[] skyboxes = new string[skyboxMaterials.Length];
			//for (int i = 0; i < skyboxMaterials.Length; i++) {
			//	skyboxes[i] = skyboxMaterials[i].name;
			//}
			//selectedSkyboxIndex = GUILayout.SelectionGrid(selectedSkyboxIndex, skyboxes, 1);
			//if (lastSelectedSkyboxIndex != selectedSkyboxIndex) {
			//	lastSelectedSkyboxIndex = selectedSkyboxIndex;
			//	metaData.skyboxIndex = selectedSkyboxIndex;
			//	//print(skyboxes[selectedSkybox]);
			//	if (selectedSkyboxIndex >= 0) {
			//		skyboxRenderer.material = skyboxMaterials[selectedSkyboxIndex];
			//	}
			//}
			//GUILayout.FlexibleSpace();
			//GUILayout.EndHorizontal();
			#endregion

			//GUILayout.BeginArea(new Rect(0, 0, 500, 250));
			GUILayout.BeginHorizontal();
			GUILayout.BeginVertical();
			//Let's control the processing blocks with our epic GUI!!!!
			//if (!Application.isEditor)
			foreach (RsProcessingBlock[] blocks in allDaBlockses) {
				foreach (RsProcessingBlock block in blocks) {
					switch (block) {
						//case RsDecimationFilter decimation:
						//	//print("decimationfilter foooound!");
						//	break;
						case RsThresholdFilter threshold:
							threshold.SetMaxDistance(GUILayout.HorizontalSlider(threshold.MaxDistance, 0.5f, 5.0f));
							break;
							//case RsPointCloud pointcloud:
							//	//print("pointcloud found!!");
							//	break;
							//default:
							//	print("da fuck. no block matched");
							//	break;
					}
				}
			}
			GUILayout.EndVertical();
			GUILayout.Space(Screen.width / 2);
			GUILayout.EndHorizontal();
		}
		GUILayout.EndVertical();
	}
		

	private class PlaylistVariables {
		public float playlistItemLabelWidth = 0;
		public bool showPopup = false;
		public MetaDataUtility.PlaylistContainer.Playlist confirmPlaylistDeletion = null;
		public int numPlaylists = 0;
		public int numTotalPlaylistItems = 0;
	}
	private readonly PlaylistVariables plVars = new PlaylistVariables();
	/// <summary>Assigned inside awake via <see cref="MetaDataUtility.LoadPlaylists"/>.</summary>
	private MetaDataUtility.PlaylistContainer playlistContainer;

	//string editableText = "Playback name";

	private void GUI_PlaylistInteractions() {
		GUILayout.BeginVertical();
		{
			if (playlistContainer == null) {
				return;
			}
			if (playlistContainer.playlists == null) {
				return;
			}

			GUILayoutOption[] addOrRemoveButtonOptions = new GUILayoutOption[] {
				GUILayout.Width(40),
				//GUILayout.Height(24),
			};

			GUIStyle labelStyle = new GUIStyle {
				//fontStyle = FontStyle.Bold,
				//fontSize = 20,
			};
			labelStyle.normal.textColor = Color.white;

			GUIContent content_NewPlaylist = new GUIContent("New Playlist");
			GUIContent content_SavePlaylists = new GUIContent("Save");

			// Calculates the width of the playlist labels for layout purposes.
			if (plVars.numTotalPlaylistItems != playlistContainer.TotalPlaylistItems()) {
				plVars.numTotalPlaylistItems = playlistContainer.TotalPlaylistItems();

				plVars.playlistItemLabelWidth = 0;
				foreach (MetaDataUtility.PlaylistContainer.Playlist playlist in playlistContainer.playlists) {
					foreach (string item in playlist.items) {
						GUIStyle style = GUI.skin.label;
						//style.alignment = TextAnchor.MiddleLeft;
						Vector2 size = style.CalcSize(new GUIContent(item));
						plVars.playlistItemLabelWidth = Mathf.Max(plVars.playlistItemLabelWidth, size.x);
					}
				}
				print(plVars.playlistItemLabelWidth);
			}

			GUIContent content_Play = new GUIContent("Play");
			GUIContent content_Pause = new GUIContent("Pause");
			GUIStyle playPauseStyle = GUI.skin.button; playPauseStyle.alignment = TextAnchor.MiddleCenter;
			Vector2 playPauseButtonSize = playPauseStyle.CalcSize(new GUIContent(content_Pause));
			GUILayoutOption[] playPauseLayout = new GUILayoutOption[] {
				GUILayout.Width(playPauseButtonSize.x * 1.5f),
				GUILayout.Height(playPauseButtonSize.y * 1.5f),
			};

			GUILayout.Space(20f);
			using (new GUILayout.HorizontalScope("Box", GUILayout.MaxWidth(Screen.width * 0.4f))) {
				using (new GUILayout.VerticalScope()) {
					using (new GUILayout.HorizontalScope()) {

						if (GUILayout.Button(content_NewPlaylist)) {
							//MetaDataUtility.PlaylistContainer.Playlist playlist = new MetaDataUtility.PlaylistContainer.Playlist();
							playlistContainer.AddPlaylist("Playlist " + (playlistContainer.playlists.Count + 1));
							//playlistContainer.
							return;
							//plVars.showPopup = true;
						}

						if (GUILayout.Button(content_SavePlaylists)) {
							MetaDataUtility.SavePlaylist(playlistContainer);
						}
						playlistAutoLoop = GUILayout.Toggle(playlistAutoLoop, "Auto Loop");
						GUILayout.FlexibleSpace();
					}
					if (playlistContainer.playlists.Count > 0) {
						GUILayout.Space(5f);
					}

					GUILayoutOption[] horizontalBoxLayout = new GUILayoutOption[] {
					GUILayout.MaxWidth(Screen.width * 0.4f),
				};

					int index = 0;
					foreach (MetaDataUtility.PlaylistContainer.Playlist playlist in playlistContainer.playlists) {
						bool thisIsCurrentlyPlaying = (playlist == currentPlaylist);

						using (new GUILayout.HorizontalScope("Box", horizontalBoxLayout)) {
							using (new GUILayout.VerticalScope()) {

								// Playlist
								using (new GUILayout.HorizontalScope()) {
									//bool playNextItem = playlistIsPlaying && thisIsCurrentlyPlaying;
									//if (playlist.items.Count > 0) {
									//	if (GUILayout.Button((playNextItem ? content_Next : content_Play), nextOrPlayLayout)) {
									//		RemoveAllDevContainers();
									//		DisposeAudio();

									//		StartPlaybackSequence(playlist);
									//	}
									//}
									GUILayout.Label(playlist.name/*, labelStyle*/);

									//playlist.name = GUILayout.TextField(playlist.name, labelStyle);
									//if (GUILayout.Button("Rename")) {
									//	plVars.showPopup = true;
									//}
									//if (showPopup) {
									//	GUILayout.Window(index, windowRect, ShowPopupGUI/* (id, playlist) => ShowPopupGUI*/, "Popup");
									//}

									//if (GUILayout.Button(playlist.isExpanded ? "^" : ">", GUILayout.Width(22f))) {
									//	playlist.isExpanded = !playlist.isExpanded;
									//	print(playlist.isExpanded);
									//}
									GUILayout.FlexibleSpace();

									if (!thisIsCurrentlyPlaying) {
										if (GUILayout.Button(plVars.confirmPlaylistDeletion == playlist ? "Confirm" : "Delete" /*"-", addOrRemoveButtonOptions*/)) {
											if (plVars.confirmPlaylistDeletion == playlist) {
												plVars.confirmPlaylistDeletion = null;
												if (currentPlaylist != playlist) {
													playlistContainer.RemovePlaylist(playlist);
													print(playlist);
													return;
												}
											}
											plVars.confirmPlaylistDeletion = playlist;
										}
									}
								}
								GUILayout.Space(5f);
								// Play/Pause, Stop and Next
								using (new GUILayout.HorizontalScope()) {
									//GUILayout.FlexibleSpace();
									bool isPaused = playbackDevice_Status == PlaybackStatus.Paused;
									bool isPlaying = playlistIsPlaying && thisIsCurrentlyPlaying && !isPaused;
									if (playlist.items.Count > 0) {
										if (GUILayout.Button((isPlaying ? content_Pause : content_Play), playPauseLayout)) {
											if (isPlaying) {
												GetPlaybackDevice(devContainers[0]).Pause();
												audioOutputDevice.Pause();
											}
											else if (isPaused) {
												GetPlaybackDevice(devContainers[0]).Resume();
												audioOutputDevice.Play();
											}
											else {
												StartPlaybackSequence(playlist);
											}
											playlistContainer.selectedPlaylist = index;
											//RemoveAllDevContainers();
											//DisposeAudio();
										}
									}
									if (isPlaying) {
										if (GUILayout.Button("Stop", playPauseLayout)) {
											DisposeAudio();
											RemoveAllDevContainers();

											currentPlaylist = null;
											playlistIsPlaying = false;
										}
										if (GUILayout.Button("Next", playPauseLayout)) {
											DisposeAudio();
											RemoveAllDevContainers();

											StartPlaybackSequence(playlist);
										}
									}
									GUILayout.FlexibleSpace();
								}
								GUILayout.Space(5f);

								// Playlist items
								//if (playlist.isExpanded) {
								if (true) {
									//bool removePlayListItem = false;
									//string playlistItemToRemove = null;
									foreach (string item in playlist.items) {
										using (new GUILayout.HorizontalScope()) {

											GUILayout.Label(item, new GUILayoutOption[] { GUILayout.Width(/*Screen.width * */ plVars.playlistItemLabelWidth * 1.05f /*0.25f*/) });
											//GUILayout.FlexibleSpace();
											if (!thisIsCurrentlyPlaying) {
												if (GUILayout.Button("Remove"/*, addOrRemoveButtonOptions*/)) {
													if (currentPlaylist != playlist) {
														playlist.RemovetItem(item);
														return;
													}
													//playlistItemToRemove = item;
													//removePlayListItem = true;
												}
											}
											GUILayout.FlexibleSpace();
											//labelStyle.fontSize = FontStyle.Normal;
										}
									}
									//if (removePlayListItem) {
									//	pl.RemovetItem(playlistItemToRemove);
									//}
									using (new GUILayout.HorizontalScope()) {
										if (!thisIsCurrentlyPlaying) {
											if (GUILayout.Button("Add", addOrRemoveButtonOptions)) {
												string dialogTitle = "Select any .bag file to add the containing folder to the Playlist";
												//string selectedFolder = OpenFolder(dialogTitle);
												//if (!string.IsNullOrEmpty(selectedFolder)) {
												//	playlist.AddItem(selectedFolder);
												//}
												SimpleFileBrowser.FileBrowser.OnCancel onCancel = () => {
													fileBrowserIsDisplayed = false;
												};
												SimpleFileBrowser.FileBrowser.OnSuccess onSuccess = (selectedFile) => {
													fileBrowserIsDisplayed = false;
													if (!string.IsNullOrEmpty(selectedFile) && File.Exists(selectedFile)) {
														playlist.AddItem(GetContainingFolder(selectedFile));
													}
												};
												SimpleFileBrowser.FileBrowser.ShowLoadDialog(onSuccess, onCancel, false, Application.dataPath, dialogTitle);
												fileBrowserIsDisplayed = true;
											}
										}
										GUILayout.FlexibleSpace();
									}
								}
							}
						}
						index++;
					}
				}
			}
		}
		GUILayout.EndVertical();
	}

	private static Vector2 popupSize = new Vector2(100, 50);
	private Rect windowRect = new Rect(Screen.width / 2 - popupSize.x / 2, Screen.height / 2 - popupSize.y / 2, popupSize.x, popupSize.y);

	[System.Obsolete("Unused")]
		//private string ShowPopupGUI(int windowID, string name) {
	private void ShowPopupGUI(int windowID/*, MetaDataUtility.PlaylistContainer.Playlist playlist*/) {
		using (new GUILayout.HorizontalScope()) {
			GUIStyle guiStyle = new GUIStyle() {
				fontStyle = FontStyle.Italic,
				//DrawWithTextSelection(new Rect(), null, 0, 0, "New Playlist".Length),
			};
			playlistContainer.playlists[windowID].name = GUILayout.TextField(playlistContainer.playlists[windowID].name, guiStyle);

			// You may put a button to close the pop up too

			if (GUILayout.Button("Save")) {
				plVars.showPopup = false;
				// you may put other code to run according to your game too
				//print(newName);
				//playlistContainer.playlists[windowID].name = newName;
				//playlist.name = newName;
				//return playlist.name
			}
			GUILayout.FlexibleSpace();
		}
		if (Input.GetKeyUp(KeyCode.Return)) {
			plVars.showPopup = !plVars.showPopup;
		}
	}

	private void PlayOrPauseSound() {
		if (audioOutputDevice.PlaybackState == PlaybackState.Playing) {
			audioOutputDevice.Pause();
			audioPlaybakActive = false;
		}
		else {
			audioOutputDevice.Play();
			audioPlaybakActive = true;
		}
	}
	private void SetSyncMarks(long audioFilePosition, ulong frameNumber) {
		audioSynced = false;
		audioSyncMark = audioFilePosition;
		pointcloudSyncMark = frameNumber;
	}

	private string GetContainingFolder(string filePath) {
		return Path.GetDirectoryName(filePath);
	}

	[System.Obsolete("Unused and deprecated", true)]
	private bool FolderContainsRecordings(string folder) {
		if (string.IsNullOrEmpty(folder)) {
			return false;
		}
		bool containsBagFiles = Directory.GetFiles(folder, "*.bag").Length > 0;
		bool containsMetaFiles = Directory.GetFiles(folder, "*.json").Length > 0;

		print(folder + " contains recordings: " + (containsBagFiles && containsMetaFiles));
		return containsBagFiles && containsMetaFiles;
	}

	[System.Obsolete("Unused and deprecated", true)]
	/// <summary>
	/// Returns the selected folder.
	/// </summary>
	private string OpenFolder(string titleMessage) {
		bool wasPlaying = false;
		if (devContainers.Count > 0) {
			using (PlaybackDevice player = GetPlaybackDevice(devContainers[0])) {
				if (player.Status == PlaybackStatus.Playing) {
					wasPlaying = true;
					player.Pause();
					audioOutputDevice.Pause();
				}
			}
		}
		string chosenFolder = null;
		//chosenFolder = FileBrowser.OpenSingleFolder(titleMessage);
		print(chosenFolder);

		if (FolderContainsRecordings(chosenFolder)) {
			print(chosenFolder + " is valid.");
			return chosenFolder;
		}

		// If the selected folder is not valid, we continue playing where we left off before.
		if (devContainers.Count > 0 && wasPlaying) {
			using (PlaybackDevice player = GetPlaybackDevice(devContainers[0])) {
				player.Resume();
				audioOutputDevice.Play();
			}
		}
		return null;
	}



	/// <summary>
	/// Add this as a checkbox to the GUI in <see cref="GUI_PlaylistInteractions"/>.
	/// </summary>
	[SerializeField]
	private bool playlistAutoLoop = true;
	private MetaDataUtility.PlaylistContainer.Playlist currentPlaylist = null;
	private bool playlistIsPlaying = false;
	private int playlistNextIndexToPlay = 0;

	[Header("Point cloud material")]
	[SerializeField] private bool scaleByDistance = false;
	[SerializeField] private float pointSize = 1.2f;
	public bool GetScaleByDistance { get { return scaleByDistance; } }
	public float GetPointSize { get { return pointSize; } }

	private void StartPlaybackSequence(MetaDataUtility.PlaylistContainer.Playlist playlist) {
		if (currentPlaylist != playlist) {
			currentPlaylist = playlist;
			playlistIsPlaying = true;
			playlistNextIndexToPlay = 0;

			if (currentPlaylist == null) {
				Debug.LogError("The Playlist is null!");
				return;
			}
		}
		if (playlistNextIndexToPlay >= currentPlaylist.items.Count) {
			print("The playlist has reached its end.");
			playlistNextIndexToPlay = 0;
			if (playlistAutoLoop) {
				playlistIsPlaying = true;
				print("Restarting playlist.");
			}
			else {
				// Prevent any form of continued playing
				currentPlaylist = null;
				playlistIsPlaying = false;
				print("Ending playlist.");
				StopAllPlayback();
				return;
			}
		}
		StartPlaying(currentPlaylist.items[playlistNextIndexToPlay]);
		playlistNextIndexToPlay++;
	}

	private void StartPlaying(string chosenFolder) {
		if (string.IsNullOrEmpty(chosenFolder)) {
			Debug.LogError("StartPlaying(chosenFolder): The folder is null or empty.");
			return;
		}

		// If something is already playing, pause it... for some reason...
		if (devContainers.Count > 0) {
			using (PlaybackDevice player = GetPlaybackDevice(devContainers[0])) {
				if (player.Status == PlaybackStatus.Playing) {
					player.Pause();
				}
			}
		}

		targetDirectory = chosenFolder;
		
		if (SkyboxControl.LoadSkybox_Event != null) {
			print("Firing [" + SkyboxControl.LoadSkybox_Event + "] event.");
			SkyboxControl.LoadSkybox_Event?.Invoke(this, chosenFolder);
		}
		LoadSessionCalibrationMetaData();
		LoadSavedMetaData();
		ApplySessionCalibration();
		audioSyncMark = metaData.audioSyncMark;
		pointcloudSyncMark = metaData.pointcloudSyncMark;

		SkyboxControl.SkyboxRotation_Event?.Invoke(this, sessionCalibration.skyboxRotationDegrees);
		if (RenderSettings.skybox) {
			RenderSettings.skybox.SetFloat(SkyboxControl._Rotation, sessionCalibration.skyboxRotationDegrees);
		}
		for (int i = 0; i < allDaBlockses.Length; i++) {
			foreach (RsProcessingBlock block in allDaBlockses[i]) {
				switch (block) {
					case RsThresholdFilter threshold: {
							threshold.SetMaxDistance(sessionCalibration.distanceThresholdValues[i]);
						}
						break;
				}
			}
		}

		try {
			string[] soundFiles = Directory.GetFiles(targetDirectory, "*.wav");
			if (soundFiles.Length > 0) {
				string audioFilePath = soundFiles[0];
				print("audioFile: " + audioFilePath);
				StartAudioFile(audioFilePath);
				audioSynced = false;
			}
			else {
				Debug.LogError("No audio file found");
			}
		}
		catch (Exception e) {
			Debug.LogError("Failed to load audio file");
			Debug.LogError(e);
		}

		RemoveAllDevContainers();
		CreatePlayers();
	}
	#endregion

	private bool IsPointInsideArea(float x, float y, Rect rect) {
		bool insideX = rect.x <= x && x <= rect.width;
		bool insideY = rect.y <= y && y <= rect.height;
		return insideX && insideY;
	}


	private PlaybackDevice GetPlaybackDevice(DevContainer devContainer) {
		return devContainer.pipe.ActiveProfile.Device.As<PlaybackDevice>();
	}

	private void KeyBoardInputInteractions() {
		if (Input.GetKeyUp(KeyCode.Escape)) {
			Application.Quit();
		}

		if (Input.GetKeyUp(KeyCode.F5)) {
			showDeveloperControls = !showDeveloperControls;
		}

		if (Input.GetKeyUp(KeyCode.Q)) {
			StopAllPipes();
		}

		if (Input.GetKeyUp(KeyCode.Z)) {
			print("toggling realtime playback");
			ToggleRealtimeMasterPlayer();
		}

		if (Input.GetKeyUp(KeyCode.F)) {
			print("fetching frameset");
			fetchNewFrameSet = true;
		}

		//Save the position calibration of this specific recording
		if (Input.GetKeyUp(KeyCode.F8)) {
			metaData.audioSyncMark = audioSyncMark;
			metaData.pointcloudSyncMark = pointcloudSyncMark;

			UpdateTransformsInMetaData();
			SaveCurrentMetaData();
		}

		//Save position calibration of this session
		if (Input.GetKeyUp(KeyCode.F9)) {
			ApplyTranformsToSessionCalibration();
			for (int i = 0; i < allDaBlockses.Length; i++) {
				foreach (RsProcessingBlock block in allDaBlockses[i]) {
					switch (block) {
						case RsThresholdFilter threshold:
							print("saving threshold values in session metadata: " + threshold.MaxDistance);
							sessionCalibration.distanceThresholdValues[i] = threshold.MaxDistance;
							break;
					}
				}
			}

			sessionCalibration.skyboxRotationDegrees = skyboxControl.SkyboxMaterial.GetFloat(SkyboxControl._Rotation);
			if (RenderSettings.skybox) {
				sessionCalibration.skyboxRotationDegrees = RenderSettings.skybox.GetFloat(SkyboxControl._Rotation);
			}
			SaveSessionCalibrationMetaData();
		}

		if (ViveInput.GetPressDown(HandRole.LeftHand, ControllerButton.Trigger) || Input.GetKeyUp(KeyCode.Space)) {
			print("toggling play/pause");
			TogglePlaybackAllPlayers();
		}
	}

	private PlaybackStatus playbackDevice_Status = PlaybackStatus.Unknown;
	void Update() {
		KeyBoardInputInteractions();

		if (currentSyncState == SyncState.MASTER_IS_WAITING) {
			foreach (MeshRenderer mr in GetComponentsInChildren<MeshRenderer>()) {
				mr.enabled = false;
			}
		}

		if (devContainers.Count > 0 && syncMethod == SyncMethod.EVERY_FRAME_TO_REFERENCE) {
			//fetch from master player
			bool masterRestarted = false;

			DevContainer masterDevContainer = devContainers[0];

			playbackDevice_Status = GetPlaybackDevice(masterDevContainer).Status;
			if (playbackDevice_Status == PlaybackStatus.Stopped) {
				print(playbackDevice_Status);
				if (playlistIsPlaying) {
					StartPlaybackSequence(currentPlaylist);
				}
				return;
			}

			if (currentSyncState == SyncState.ALL_PLAYERS_ARE_CAUGHT_UP && playbackDevice_Status == PlaybackStatus.Paused) {
				currentSyncState = SyncState.PLAYING;

				foreach(MeshRenderer mr in GetComponentsInChildren<MeshRenderer>()) {
					mr.enabled = true;
				}
				GetPlaybackDevice(masterDevContainer).Resume();
				print("Resuming master device since all slaves have caught up");
			}


			masterDevContainer.newFrameAvailable = false;
			try {
				if (GetPlaybackDevice(masterDevContainer).Realtime || fetchNewFrameSet) {
					if (masterDevContainer.pipe.PollForFrames(out FrameSet frames)) {
						fetchNewFrameSet = false;
						using (frames) {
							ulong frameNumber = frames.Number;

							if (masterDevContainer.latestFrameSet != null && frameNumber < masterDevContainer.latestFrameSet.Number) {
								masterRestarted = true;
								currentSyncState = SyncState.MASTER_IS_WAITING;
								audioSynced = false;
								audioOutputDevice.Pause();

								print("frameNumber:" + frameNumber + " latestFrameNumber:" + masterDevContainer.latestFrameSet.Number);
								print("master Restarted");
							}

							if (currentSyncState == SyncState.MASTER_IS_WAITING && playbackDevice_Status == PlaybackStatus.Playing) {
								GetPlaybackDevice(masterDevContainer).Pause();
								print("PAUSING MASTER DEVICE TO WAIT FOR SLAVES");
							}

							if (masterDevContainer.latestFrameSet != null) {
								masterDevContainer.latestFrameSet.Dispose();
								masterDevContainer.latestFrameSet = null;
							}
							masterDevContainer.latestFrameSet = frames.AsFrameSet();

							masterDevContainer.newFrameAvailable = true;

							if (audioFile != null && !audioSynced && masterDevContainer.latestFrameSet.Number >= pointcloudSyncMark) {
								audioSynced = true;
								// Start realtime in pointcloud and start from sync position in audio
								audioFile.Position = audioSyncMark;
								audioOutputDevice.Play();
								print("SYNCING AUDIO TO POINTCLOUD!");
							}
						}
					}
				}
			} catch (Exception e) {
				Debug.LogError(e);
			}

			// Loop through the subordinate devices
			bool tempSyncedState = true;
			if (masterDevContainer.newFrameAvailable || playbackDevice_Status == PlaybackStatus.Paused) {
				for (int i = 1; i < devContainers.Count; i++) {
					DevContainer devContainer = devContainers[i];

					if (masterRestarted) {
						NonRealTimeRestart(ref devContainer);
						devContainer.currentRecordingFrameOffsetIndex = 0;
					}

					// THIS IS INSAAAAANE!!!!!!!! //Gunnar
					// I concur. //Mike
					// Explanation. This code block is continuously trying to sync with the relevant offset in the metadata. The metadata has different offsets saved throughout the recording.
					if (masterDevContainer.latestFrameSet.Number > devContainer.recordingFrameIndexOffsets[devContainer.currentRecordingFrameOffsetIndex].masterFrameNumber) {
						if (devContainer.currentRecordingFrameOffsetIndex < devContainer.recordingFrameIndexOffsets.Count - 1)
							devContainer.currentRecordingFrameOffsetIndex++;
					}
					ulong goalFrameIndex = masterDevContainer.latestFrameSet.Number + devContainer.recordingFrameIndexOffsets[devContainer.currentRecordingFrameOffsetIndex].offset;

					devContainer.newFrameAvailable = false;
					for (int j = 0; j < 50; j++) {

						if (devContainer.latestFrameSet != null && devContainer.latestFrameSet.Number >= goalFrameIndex && devContainer.latestFrameSet.Number < goalFrameIndex + 5) {
							devContainer.skippedFramesLastPoll = j;
							break;
						}
						tempSyncedState = false;

						try {
							if (devContainer.pipe.PollForFrames(out FrameSet frames)) {
								using (frames) {

									if (devContainer.latestFrameSet != null) {
										devContainer.latestFrameSet.Dispose();
										devContainer.latestFrameSet = null;
									}
									devContainer.latestFrameSet = frames.AsFrameSet();

									devContainer.newFrameAvailable = true;
								}
							}
						}
						catch (Exception e) {
							print(e);
						}
					}
				}
				if (tempSyncedState && currentSyncState == SyncState.MASTER_IS_WAITING) {
					currentSyncState = SyncState.ALL_PLAYERS_ARE_CAUGHT_UP;
				}
			}
		}
		else {
			for (int i = 0; i < devContainers.Count; i++) {
				DevContainer devContainer = devContainers[i];
				devContainer.newFrameAvailable = false;
				try {
					if (devContainer.pipe.PollForFrames(out FrameSet frames)) {
						using (frames) {
							ulong frameNumber = frames.Number;
							double stamp = frames.Timestamp;

							if (devContainer.latestFrameSet != null) {
								devContainer.latestFrameSet.Dispose();
								devContainer.latestFrameSet = null;
							}
							devContainer.latestFrameSet = frames.AsFrameSet();
						}
					}
				}
				catch (Exception e) {
					Debug.LogError(e);
				}
			}
		}
	}
	

	private void StartAudioFile(string path) {
		DisposeAudio();

		audioFile = new AudioFileReader(path);
		float[] audioSampleBlock = new float[512];
		ISampleProvider monoAudio = audioFile.ToMono();

		int playHead = 0;
		int nrSamplesRead = monoAudio.Read(audioSampleBlock, 0, audioSampleBlock.Length);
		WaveFormat format = audioFile.WaveFormat;
		print("totaltime: " + audioFile.TotalTime.TotalSeconds);
		print("sampleRate: " + format.SampleRate);
		double nrSamples = (audioFile.TotalTime.TotalSeconds * (double)format.SampleRate);
		print("nrSamples: " + nrSamples);
		float samplesPerPixel = (float)(nrSamples / audioWaveformTexture.width);
		print("samplesPerPixel: " + samplesPerPixel);

		while (nrSamplesRead > 0) {
			
			for (int deltaX = 0; deltaX < audioSampleBlock.Length; deltaX++) {
				int x = (int)((deltaX + playHead) / samplesPerPixel);
				int y = (int)(audioWaveformTexture.height / 2) + (int)(audioSampleBlock[deltaX] * audioWaveformTexture.height / 2);

				audioWaveformTexture.SetPixel(x, y, Color.Lerp(Color.yellow, Color.red, 0.5f));
			}

			nrSamplesRead = monoAudio.Read(audioSampleBlock, 0, audioSampleBlock.Length);
			playHead += nrSamplesRead;
		}

		audioWaveformTexture.Apply();
		audioFile.Position = 0;

		audioOutputDevice = new WaveOutEvent();
		audioOutputDevice.DesiredLatency = 100;
		audioOutputDevice.Init(audioFile);
	}


	private void CreatePlayers() {
		//Add more devContainers if needed.
		int devsToAdd = metaData.rows.Count - devContainers.Count;
		for (int i = 0; i < devsToAdd; i++) {
			DevContainer devContainer = new DevContainer();
			devContainers.Add(devContainer);
		}

		GC.Collect();

		FrameRelay[] frameRelays = GetComponentsInChildren<FrameRelay>();
		TrackerTransform[] trackerTransforms = GetComponentsInChildren<TrackerTransform>();

		for (int i = 0; i < metaData.rows.Count; i++) {
			trackerTransforms[i].enabled = false;

			if (metaData.rows[i].deviceTransform.position.magnitude > 0.000f) {
				trackerTransforms[i].transform.position = metaData.rows[i].deviceTransform.position;
			}
			metaData.rows[i].deviceTransform.rotation.ToAngleAxis(out float angle, out Vector3 axis);
			if (angle > 0.000f) {
				trackerTransforms[i].transform.rotation = metaData.rows[i].deviceTransform.rotation;
			}

			string serialNumber = metaData.rows[i].serialNumber;
			devContainers[i].serialNumber = serialNumber;
			devContainers[i].recordingFrameIndexOffsets = new List<MetaData.Row.RecordingFrameIndexOffset>(metaData.rows[i].recordingFrameIndexOffsets);

			devContainers[i].pipe = new Pipeline(ctx);
			Config cfg = new Config();

			string playbackPath = Path.Combine(targetDirectory, serialNumber + ".bag");
			cfg.EnableDeviceFromFile(playbackPath);

			devContainers[i].pipe.Start(cfg);

			if (syncMethod == SyncMethod.EVERY_FRAME_TO_REFERENCE) {
				if (i > 0) {
					GetPlaybackDevice(devContainers[i]).Realtime = false;
				}
			}

			print(playbackPath);

			try {
				frameRelays[i].setDevContainer(devContainers[i]);
			}
			catch (Exception e) {
				Debug.LogError("probably no frameRelay available for assigning current devcontainer");
				Debug.LogError(e);
			}
		}
		currentSyncState = SyncState.MASTER_IS_WAITING;
	}

	private void StopAllPlayback() {
		RemoveAllDevContainers();
		DisposeAudio();
		RenderSettings.skybox = null;
		foreach (MeshRenderer mr in GetComponentsInChildren<MeshRenderer>()) {
			mr.enabled = false;
		}
	}
	
	private void DisposeAudio() {
		if (audioFile != null) {
			audioFile.Close();
			audioFile.Dispose();
			audioFile = null;
		}
		if (audioOutputDevice != null) {
			if (audioOutputDevice.PlaybackState != PlaybackState.Stopped) {
				audioOutputDevice.Stop();
			}
			audioOutputDevice.Dispose();
			audioOutputDevice = null;
		}
	}
	private void RemoveAllDevContainers() {
		StopAllPipes();
		devContainers.Clear();
	}

	///<summary>Be aware! The fucking API hangs if calling stop on a playback device which is in non real time mode. So we turn on real time and wait until we start receiving frames again. Then we stop the pipe.</summary>
	private void StopAllPipes() {
		print("Stopping all pipes!");
		foreach (DevContainer devContainer in devContainers) {
			try {
				//Whaaaat the fuck is going on??? Don't know if this code is weird because the API is buggy as hell or if it's just fucked up by me (Gunnar)
				if (devContainer.pipe.ActiveProfile.Device.Is(Extension.Playback) &&
					!devContainer.pipe.ActiveProfile.Device.As<PlaybackDevice>().Realtime) {

					devContainer.pipe.ActiveProfile.Device.As<PlaybackDevice>().Realtime = true;
					print("setting to realtime & waiting for frames");
					for (int j = 0; j < 5; j++) {
						devContainer.pipe.WaitForFrames();
					}
				}

				devContainer.pipe.Stop();
				devContainer.pipe.Dispose();

				print("stopped dev: " + devContainer.serialNumber);
			}
			catch (Exception e) {
				Debug.LogError(e);
			}
		}
		GC.Collect();
	}

	public void TogglePlaybackAllPlayers() {
		foreach (DevContainer d in devContainers) {
			using (PlaybackDevice p = GetPlaybackDevice(d)) {
				if (p.Realtime) {
					if (p.Status == PlaybackStatus.Playing) {
						p.Pause();
						audioOutputDevice.Pause();
					}
					else {
						p.Resume();
						audioOutputDevice.Play();
					}
				}
			}
		}
	}

	public void RestartMasterPlayer() {
		using (PlaybackDevice playback = PlaybackDevice.FromDevice(devContainers[0].pipe.ActiveProfile.Device)) {
			playback.Position = 0;
		}
	}

	private void NonRealTimeRestart(ref DevContainer devContainer) {
		print("nonrealtimerestart");
		int i = 0;
		while (i < 7000) {
			try {
				if (devContainer.pipe.PollForFrames(out FrameSet frames)) {
					using (frames) {
						ulong frameNumber = frames.Number;
						print("frameNumber:" + frameNumber);
						if (frameNumber < 10) {
							devContainer.latestFrameSet = frames.AsFrameSet();
							devContainer.skippedFramesLastPoll = i;
							print("skipped " + i + " frames to restart the playback");
							return;
						}
					}
					i++;
				}
			}
			catch (Exception e) {
				print(e);
			}
		}
		print("iterated " + i + " times");
	}

	public void ToggleRealtimeMasterPlayer() {
		if (devContainers.Count > 0 && devContainers[0] != null) {
			using (PlaybackDevice player = GetPlaybackDevice(devContainers[0])) {
				player.Realtime = !player.Realtime;
			}
		}
	}
	
	private void UpdateTransformsInMetaData() {
		TrackerTransform[] trackerTransforms = GetComponentsInChildren<TrackerTransform>();
		for (int i = 0; i < devContainers.Count; i++) {
			TrackerTransform tt = trackerTransforms[i];
			metaData.rows[i].deviceTransform.rotation = tt.transform.rotation;
			metaData.rows[i].deviceTransform.position = tt.transform.position;
		}
	}

	private void ApplyTranformsToSessionCalibration() {
		TrackerTransform[] trackerTransforms = GetComponentsInChildren<TrackerTransform>();
		sessionCalibration.rows.Clear();
		for (int i = 0; i < devContainers.Count; i++) {
			TrackerTransform tt = trackerTransforms[i];

			sessionCalibration.rows.Add(new MetaData.Row {
				deviceTransform = new MetaData.Row.DeviceTransform {
					rotation = tt.transform.rotation,
					position = tt.transform.position
				},
				serialNumber = devContainers[i].serialNumber,
			});
		}
	}

	private void SaveCurrentMetaData() {
		string metaDataString = JsonUtility.ToJson(metaData, true);
		print(metaDataString);
		//Extract the foldername from the path
		string recordingName = Path.GetFileName(targetDirectory);
		print("recordingName: " + recordingName);
		string metaDataPath = Path.Combine(targetDirectory, recordingName + ".json");
		print("metadata path: " + metaDataPath);
		if (File.Exists(metaDataPath)) {
			File.Delete(metaDataPath);
		}
		File.WriteAllText(metaDataPath, metaDataString);
	}

	private void LoadSavedMetaData() {
		//Load saved metadata
		try {
			string recordingName = Path.GetFileName(targetDirectory);
			string filePath = Path.Combine(targetDirectory, recordingName + ".json");
			if (File.Exists(filePath)) {
				string metaDataString = File.ReadAllText(filePath);
				metaData = JsonUtility.FromJson<MetaData>(metaDataString);
			}
			else {
				string[] jsonFiles = Directory.GetFiles(targetDirectory, "*.json");
				if (jsonFiles.Length > 0) {
					filePath = jsonFiles[0];
					string metaDataString = File.ReadAllText(filePath);
					metaData = JsonUtility.FromJson<MetaData>(metaDataString);
				}
			}
			print("Loading metadata from: " + filePath);
		}
		catch (Exception e) {
			Debug.LogError(e);
			return;
		}
	}

	private const string _SessionCalibration = "sessionCalibration.json";

	private void SaveSessionCalibrationMetaData() {
		string metaDataString = JsonUtility.ToJson(sessionCalibration, true);
		print(metaDataString);
		DirectoryInfo sessionDir = Directory.GetParent(targetDirectory);
		string metaDataPath = Path.Combine(sessionDir.ToString(), _SessionCalibration);
		print("Path to sessionCalibration: " + metaDataPath);
		if (File.Exists(metaDataPath)) {
			File.Delete(metaDataPath);
		}
		File.WriteAllText(metaDataPath, metaDataString);
	}

	private void LoadSessionCalibrationMetaData() {
		print("Trying to load sessionCalibration!");
		//Load saved metadata
		try {
			DirectoryInfo sessionDir = Directory.GetParent(targetDirectory);
			
			string filePath = Path.Combine(sessionDir.FullName, _SessionCalibration);
			if (!File.Exists(filePath)) {
				Debug.LogError("didn't find any sessionCalibration file");
				return;
			}
			string metaDataString = File.ReadAllText(filePath);
			if (metaDataString == null || metaDataString == "") {
				Debug.LogError("session calibration file was empty!!!");
			}

			print("path to sessionCalibration: " + sessionDir.FullName);

			sessionCalibration = JsonUtility.FromJson<MetaData>(metaDataString);
		}
		catch (Exception e) {
			Debug.LogError(e);
			return;
		}
	}

	private void ApplySessionCalibration() {
		if (sessionCalibration != null) {
			for (int i = 0; i < sessionCalibration.rows.Count; i++) {
				if (metaData.rows[i].deviceTransform.position == Vector3.zero) {
					metaData.rows[i].deviceTransform = sessionCalibration.rows[i].deviceTransform;
				}
			}
		}
	}

	void OnDestroy() {
		StopAllPipes();
		DisposeAudio();
	}

	void OnApplicationQuit() {
		print("OnApplicationQuit()");
		DisposeAudio();
	}

	[Serializable]
	public class DevContainer {
		public Pipeline pipe;
		public List<MetaData.Row.RecordingFrameIndexOffset> recordingFrameIndexOffsets;
		public int currentRecordingFrameOffsetIndex = 0;
		public int skippedFramesLastPoll;
		public string serialNumber;
		public bool newFrameAvailable = false;
		public FrameSet latestFrameSet;
	}

	[Serializable] [System.Obsolete("No vive tracker positioning used anymore", true)]
	public class CameraTrackerBinding {
		public string camera;
		public string tracker;
	}

	[Serializable]
	public class MetaData {
		[Serializable]
		public class Row {
			[Serializable]
			public struct DeviceTransform {
				public Quaternion rotation;
				public Vector3 position;
			}
			[Serializable]
			public struct RecordingFrameIndexOffset {
				public ulong masterFrameNumber;
				public ulong offset;
			}

			public DeviceTransform deviceTransform;
			public string serialNumber;
			public List<RecordingFrameIndexOffset> recordingFrameIndexOffsets = new List<RecordingFrameIndexOffset>();
		}

		public List<Row> rows = new List<Row>();
		public long audioSyncMark;
		public ulong pointcloudSyncMark;
		/// <summary>
		/// Replaced by <see cref="skyboxRotationDegrees"/>.
		/// </summary>
		[System.Obsolete("Replaced with skyboxRotationDegrees.", true)]
		public int skyboxIndex = -1;
		/// <summary>
		/// Replaced by <see cref="skyboxRotationDegrees"/>.
		/// </summary>
		[System.Obsolete("Replaced with skyboxRotationDegrees.", true)]
		public Quaternion skyboxRotation = Quaternion.identity;
		public float skyboxRotationDegrees;
		public float[] distanceThresholdValues = { 2, 2, 2, 2 };
	}
}
