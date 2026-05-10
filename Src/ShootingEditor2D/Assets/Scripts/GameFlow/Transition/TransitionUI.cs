using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FrameworkDesign;

namespace GameFlow
{
    public class TransitionUI : MonoSingleton<TransitionUI>
    {
        [Header("Loading")]
        [SerializeField] private CanvasGroup loadingCanvasGroup;
        [SerializeField] private Slider progressBar;
        [SerializeField] private TMP_Text progressText;

        [Header("Error")]
        [SerializeField] private GameObject errorPanel;
        [SerializeField] private TMP_Text errorText;

        [Header("Fade")]
        [SerializeField] private float fadeDuration = 0.3f;

        protected void Awake()
        {
            DontDestroyOnLoad(gameObject);

            loadingCanvasGroup.alpha = 0;
            loadingCanvasGroup.blocksRaycasts = false;

            errorPanel.SetActive(false);
        }

        public IEnumerator FadeIn()
        {
            loadingCanvasGroup.blocksRaycasts = true;
            yield return Fade(0f, 1f);
        }

        public IEnumerator FadeOut()
        {
            yield return Fade(1f, 0f);
            loadingCanvasGroup.blocksRaycasts = false;
        }

        public void SetProgress(float progress, string text)
        {
            progressBar.value = progress;
            progressText.text = text;
        }

        public void ShowError(string message)
        {
            errorPanel.SetActive(true);
            errorText.text = message;
        }

        private IEnumerator Fade(float startAlpha, float endAlpha)
        {
            float time = 0f;
            while (time < fadeDuration)
            {
                time += Time.deltaTime;
                loadingCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, time / fadeDuration);
                yield return null;
            }
            loadingCanvasGroup.alpha = endAlpha;
        }
    }
}
