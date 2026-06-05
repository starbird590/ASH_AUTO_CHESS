using UnityEngine;

[DisallowMultipleComponent]
public class StageMapUIView : MonoBehaviour
{
    private static StageMapUIView instance;

    [Header("Map Window")]
    [SerializeField] private GameObject mapRoot;

    public static StageMapUIView Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindLoadedInstance();
            }

            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[StageMapUIView] Multiple map views found. The latest enabled view will be used.");
        }

        instance = this;
        EnsureMapRoot();
    }

    private void OnEnable()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Reset()
    {
        mapRoot = gameObject;
    }

    public void ShowMap()
    {
        EnsureMapRoot();
        mapRoot.SetActive(true);
        MapNodeButtonUI.RefreshAllButtons();
    }

    public void HideMap()
    {
        EnsureMapRoot();
        mapRoot.SetActive(false);
    }

    private void EnsureMapRoot()
    {
        if (mapRoot == null)
        {
            mapRoot = gameObject;
        }
    }

    private static StageMapUIView FindLoadedInstance()
    {
        StageMapUIView[] views = Resources.FindObjectsOfTypeAll<StageMapUIView>();
        for (int i = 0; i < views.Length; i++)
        {
            StageMapUIView view = views[i];
            if (view == null || view.hideFlags != HideFlags.None || !view.gameObject.scene.IsValid())
            {
                continue;
            }

            return view;
        }

        return null;
    }
}
