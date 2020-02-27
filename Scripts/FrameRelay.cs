using UnityEngine;
using System;
using Intel.RealSense;

public class FrameRelay : RsFrameProvider {
	
	[SerializeField]
	private Bodyghost.DevContainer devContainer;

	private bool streaming = false;

	//RsFrameProvider stuff
	public override event Action<PipelineProfile> OnStart;
	public override event Action OnStop;
	public override event Action<Frame> OnNewSample;

	private Bodyghost bodyghost;
	private MeshRenderer meshRenderer;
	
	void Start() {
		bodyghost = GetComponentInParent<Bodyghost>();
		meshRenderer = GetComponentInChildren<MeshRenderer>();

		meshRenderer.material = Instantiate(meshRenderer.material);
		RsStreamTextureRenderer rsstr = GetComponentInChildren<RsStreamTextureRenderer>();
		rsstr.textureBinding.AddListener((texture) => { meshRenderer.material.mainTexture = texture; });
	}

	public void setDevContainer(Bodyghost.DevContainer devContainer) {
		int myIndex = this.transform.GetSiblingIndex();
		print("Setting devContainer (with serial " + devContainer.serialNumber +") in frameRelay " + myIndex);
		this.devContainer = devContainer;
		OnStop?.Invoke();
		streaming = false;
	}

	private void OnDisable() {
		OnStop?.Invoke();
		streaming = false;
	}

	void Update() {
		if(devContainer == null) {
			return;
		}
		if (!streaming && devContainer.newFrameAvailable && devContainer.pipe.ActiveProfile != null) {
			OnStart?.Invoke(devContainer.pipe.ActiveProfile);
			streaming = true;
		}
		if (streaming && devContainer.newFrameAvailable) {
			OnNewSample?.Invoke(devContainer.latestFrameSet);
		}

		meshRenderer.material.SetFloat("_PointSize", bodyghost.GetPointSize);
		meshRenderer.material.SetFloat("_UseDistance", bodyghost.GetScaleByDistance ? 1 : 0);
	}
}
