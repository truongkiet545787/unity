using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shield : MonoBehaviour
{
    Renderer _renderer;
    [SerializeField] AnimationCurve _DisplacementCurve;
    [SerializeField] float _DisplacementMagnitude;
    [SerializeField] float _LerpSpeed;
    [SerializeField] float _DisolveSpeed;
    [Tooltip("Chỉ bật cho SampleScene của asset. PoseKart điều khiển Shield qua API runtime.")]
    [SerializeField] bool _enableDemoInput;
    bool _shieldOn;
    Coroutine _disolveCoroutine;
    // Start is called before the first frame update
    void Start()
    {
        _renderer = GetComponent<Renderer>();
    }

    // Update is called once per frame
    void Update()
    {
        // The original asset listens to mouse/F for its demo scene. Do not let
        // that input path run inside PoseKart, especially with the Input System.
        if (!_enableDemoInput)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit))
            {
                HitShield(hit.point);
            }
        }
        if (Input.GetKeyDown(KeyCode.F))
        {
            OpenCloseShield();
        }
    }

    public void HitShield(Vector3 hitPos)
    {
        EnsureRenderer();
        if (_renderer == null)
        {
            return;
        }

        _renderer.material.SetVector("_HitPos", hitPos);
        StopAllCoroutines();
        StartCoroutine(Coroutine_HitDisplacement());
    }

    public void OpenCloseShield()
    {
        SetShieldVisible(!_shieldOn, true);
    }

    /// <summary>
    /// Runtime-safe API used by PoseKart's authoritative shield visual flow.
    /// This keeps the sample asset's dissolve animation without relying on its
    /// mouse/F demo controls.
    /// </summary>
    public void SetShieldVisible(bool visible, bool animate)
    {
        EnsureRenderer();
        if (_renderer == null)
        {
            return;
        }

        _shieldOn = visible;
        if (_disolveCoroutine != null)
        {
            StopCoroutine(_disolveCoroutine);
        }

        float target = visible ? 1f : 0f;
        if (!animate || !_renderer.material.HasProperty("_Disolve"))
        {
            if (_renderer.material.HasProperty("_Disolve"))
            {
                _renderer.material.SetFloat("_Disolve", target);
            }
            _disolveCoroutine = null;
            return;
        }

        _disolveCoroutine = StartCoroutine(Coroutine_DisolveShield(target));
    }

    public bool IsShieldVisible => _shieldOn;

    private void EnsureRenderer()
    {
        if (_renderer == null)
        {
            _renderer = GetComponent<Renderer>();
        }
    }

    IEnumerator Coroutine_HitDisplacement()
    {
        EnsureRenderer();
        if (_renderer == null || !_renderer.material.HasProperty("_DisplacementStrength"))
        {
            yield break;
        }

        float lerp = 0;
        while (lerp < 1)
        {
            _renderer.material.SetFloat("_DisplacementStrength", _DisplacementCurve.Evaluate(lerp) * _DisplacementMagnitude);
            lerp += Time.deltaTime*_LerpSpeed;
            yield return null;
        }
    }

    IEnumerator Coroutine_DisolveShield(float target)
    {
        EnsureRenderer();
        if (_renderer == null || !_renderer.material.HasProperty("_Disolve"))
        {
            yield break;
        }

        float start = _renderer.material.GetFloat("_Disolve");
        float lerp = 0;
        while (lerp < 1)
        {
            _renderer.material.SetFloat("_Disolve", Mathf.Lerp(start,target,lerp));
            lerp += Time.deltaTime * _DisolveSpeed;
            yield return null;
        }
    }
}
