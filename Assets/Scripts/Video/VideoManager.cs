using UnityEngine;
using UnityEngine.Video;

namespace Assets.Scripts.Video
{
    public class VideoManager : MonoBehaviour
    {
        public VideoPlayer videoPlayer;
        public VideoPlayer videoPlayerLoad;
        public GameObject videoPanel;
        public GameObject loaderPanel;

        void Start()
        {
            string videoPath = System.IO.Path.Combine(
                Application.streamingAssetsPath,
                "videoplayback.mp4"
            );

            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = videoPath;

            videoPlayer.loopPointReached += OnVideoFinished;

            loaderPanel.SetActive(false);
            videoPanel.SetActive(true);

            videoPlayer.Play();
        }

        void OnVideoFinished(VideoPlayer vp)
        {
            string videoPathLoad = System.IO.Path.Combine(
                Application.streamingAssetsPath,
                "0719.mp4"
            );

            videoPlayerLoad.source = VideoSource.Url;
            videoPlayerLoad.url = videoPathLoad;

            videoPlayerLoad.loopPointReached += OnVideoFinished;

            videoPanel.SetActive(false);
            loaderPanel.SetActive(true);
        }
    }
}