using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;

public class MetaDataUtility {

	private const string _PlaylistFileName = "Playlists.json";

	[Serializable]
	public class PlaylistContainer {
		[Serializable]
		public class Playlist {
			/// <summary>Name of the playlist.</summary>
			public string name;
			public List<string> items = new List<string>();

			public bool AddItem(string item) {
				if (!items.Contains(item) && !string.IsNullOrEmpty(item)) {
					items.Add(item);
					return true;
				}
				return false;
			}
			public bool RemovetItem(string item) {
				if (items.Contains(item)) {
					items.Remove(item);
					return true;
				}
				return false;
			}
			public Playlist(string name) {
				this.name = name;
			}
		}
		public List<Playlist> playlists = new List<Playlist>();
		public int selectedPlaylist = 0;

		public void AddPlaylist(string name) {
			playlists.Add(new Playlist(name));
		}
		public void RemovePlaylist(Playlist playlist) {
			playlists.Remove(playlist);
		}
		public int TotalPlaylistItems() {
			int count = 0;
			foreach (Playlist playlist in playlists) {
				foreach (string item in playlist.items) {
					count++;
				}
			}
			return count;
		}
	}

	public static void SavePlaylist(PlaylistContainer playlists) {
		DirectoryInfo dataPath = new DirectoryInfo(Application.dataPath).Parent;

		string jsonData = JsonUtility.ToJson(playlists, true);
		Debug.Log(jsonData);

		string filePath = Path.Combine(dataPath.FullName, _PlaylistFileName);

		if (File.Exists(filePath)) {
			File.Delete(filePath);
		}
		File.WriteAllText(filePath, jsonData);
	}
	public static PlaylistContainer LoadPlaylists() {
		DirectoryInfo dataPath = new DirectoryInfo(Application.dataPath).Parent;
		PlaylistContainer playlistContainer = null;
		string jsonData = null;
		string filePath = Path.Combine(dataPath.FullName, _PlaylistFileName);

		if (File.Exists(filePath)) {
			jsonData = File.ReadAllText(filePath);
			if (string.IsNullOrEmpty(jsonData)) {
				playlistContainer = new PlaylistContainer();
			}
			playlistContainer = JsonUtility.FromJson<PlaylistContainer>(jsonData);
		}
		else {
			playlistContainer = new PlaylistContainer();
		}
		Debug.Log("LoadPlaylists: " + jsonData);
		return playlistContainer;
	}
}