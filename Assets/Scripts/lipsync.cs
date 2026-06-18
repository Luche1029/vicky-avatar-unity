using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Lip-sync a visemi per personaggi Character Creator.
/// Legge una timeline Rhubarb (mouthCues) e pilota i blendshape V_ in sync con l'audio.
///
/// Setup:
///   1. Metti il WAV del TTS e il vicky.json in Assets (il .json diventa un TextAsset).
///   2. Aggiungi questo script a un GameObject (es. la root del personaggio).
///      L'AudioSource viene aggiunto in automatico.
///   3. Nell'Inspector assegna:
///        - Face Renderer  -> lo SkinnedMeshRenderer con i visemi (CC_Base_Body)
///        - Clip           -> il WAV
///        - Rhubarb Json   -> il vicky.json
///   4. (Test) imposta l'AudioSource su Spatial Blend = 2D, così lo senti a prescindere dalla posizione.
///   5. Play.
///
/// In produzione: invece di asset pre-assegnati, alimenterai audio + cue a runtime
/// (l'output di /tts + Rhubarb dal backend) chiamando PlayRuntime(clip, json).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class LipSync : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Lo SkinnedMeshRenderer con i blendshape V_ (es. CC_Base_Body).")]
    public SkinnedMeshRenderer faceRenderer;

    [Tooltip("Il WAV del TTS (per il test).")]
    public AudioClip clip;

    [Tooltip("Il vicky.json di Rhubarb, importato come TextAsset (per il test).")]
    public TextAsset rhubarbJson;

    [Tooltip("Il testo per la visualizzazione dei fonemi Rhubarb")]
    public TMP_Text text;

    [Header("Tuning")]
    [Tooltip("Velocita' di easing verso la shape target. Piu' alto = piu' reattivo (e potenzialmente scattoso).")]
    [Range(1f, 30f)] public float blendSpeed = 12f;

    [Tooltip("Moltiplicatore globale sull'apertura bocca. <1 = parla piu' chiuso, >1 = piu' marcato.")]
    [Range(0.3f, 1.5f)] public float intensity = 1f;

    public bool playOnStart = true;

    // ---------- Strutture JSON Rhubarb ----------
    [Serializable] public class MouthCue { public float start; public float end; public string value; }
    [Serializable] public class RhubarbData { public MouthCue[] mouthCues; }

    // ---------- Mappatura Rhubarb (A..H, X) -> visemi CC (nome, peso 0..100). TARABILE. ----------
    static readonly Dictionary<string, (string name, float weight)[]> Map =
        new Dictionary<string, (string, float)[]>
    {
        { "A", new[] { ("V_Explosive",      100f) } },    
        { "B", new[] { ("V_Open",            17f),          
                        ("V_VWide",         100f) } },       
        { "C", new[] { ("V_Open",            29f) } },    
        { "D", new[] { ("V_Open",            11f),          
                        ("V_VWide",         100f)} },   
        { "F", new[] { ("V_Open",            15f),          
                        ("V_Tongue_Raise",   45f), 
                        ("V_Tongue_Curl_U",  55f)} },                           
        { "E", new[] { ("V_Tight",          100f) } },      
        { "G", new[] { ("V_Dental_Lip",     100f) } },          
        { "H", new[] { ("V_VWide",          100f) } },       
        { "X", new (string, float)[0] },                   
    };

    AudioSource _audio;
    MouthCue[] _cues;
    int[] _managed;                                       // indici blendshape gestiti (unici)
    float[] _cur;                                         // peso corrente (parallelo a _managed)
    float[] _target;                                      // peso target del frame
    Dictionary<string, (int slot, float weight)[]> _compiled; // letter -> (posizione in _managed, peso)
    int _cursor;

    void Awake()
    {
        _audio = GetComponent<AudioSource>();

        if (faceRenderer == null)
        {
            Debug.LogError("[LipSync] faceRenderer non assegnato.");
            enabled = false;
            return;
        }

        if (clip != null) _audio.clip = clip;
        if (rhubarbJson != null) LoadCues(rhubarbJson.text);

        Compile();
    }

    void Start()
    {
        if (playOnStart && _audio.clip != null) _audio.Play();
    }

    void Update()
    {
        if (_managed == null || _managed.Length == 0 || _cues == null || _cues.Length == 0) return;

        // shape corrente in base al tempo dell'audio; a riposo quando non sta suonando
        string shape = _audio.isPlaying ? CurrentShape(_audio.time) : "X";
        text.text = shape;

        // target: 0 ovunque, poi applica la shape corrente
        for (int i = 0; i < _target.Length; i++) _target[i] = 0f;
        if (_compiled.TryGetValue(shape, out var pairs))
            foreach (var (slot, w) in pairs) _target[slot] = w * intensity;

        // easing frame-rate independent: cur -> target
        float k = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
        for (int i = 0; i < _managed.Length; i++)
        {
            _cur[i] = Mathf.Lerp(_cur[i], _target[i], k);
            faceRenderer.SetBlendShapeWeight(_managed[i], _cur[i]);
        }
    }

    /// <summary>Trova la shape Rhubarb attiva al tempo t, avanzando un cursore (efficiente su clip lunghi).</summary>
    string CurrentShape(float t)
    {
        if (_cursor >= _cues.Length) _cursor = 0;
        if (t < _cues[_cursor].start) _cursor = 0;                 // audio ripartito da capo
        while (_cursor < _cues.Length - 1 && t >= _cues[_cursor].end) _cursor++;
        var c = _cues[_cursor];
        return (t >= c.start && t < c.end) ? c.value : "X";
    }

    void LoadCues(string json)
    {
        var data = JsonUtility.FromJson<RhubarbData>(json);
        _cues = data != null ? data.mouthCues : null;
    }

    /// <summary>Risolve i nomi dei blendshape in indici una sola volta.</summary>
    void Compile()
    {
        var mesh = faceRenderer.sharedMesh;
        var indexOfSlot = new List<int>();
        var slotOfIndex = new Dictionary<int, int>();
        _compiled = new Dictionary<string, (int, float)[]>();

        foreach (var kv in Map)
        {
            var pairs = new List<(int, float)>();
            foreach (var (name, weight) in kv.Value)
            {
                int idx = mesh.GetBlendShapeIndex(name);
                if (idx < 0)
                {
                    Debug.LogWarning($"[LipSync] Blendshape non trovato: {name}");
                    continue;
                }
                if (!slotOfIndex.TryGetValue(idx, out int slot))
                {
                    slot = indexOfSlot.Count;
                    slotOfIndex[idx] = slot;
                    indexOfSlot.Add(idx);
                }
                pairs.Add((slot, weight));
            }
            _compiled[kv.Key] = pairs.ToArray();
        }

        _managed = indexOfSlot.ToArray();
        _cur = new float[_managed.Length];
        _target = new float[_managed.Length];
    }

    // ---------- API runtime (per la pipeline col backend, piu' avanti) ----------

    /// <summary>Riproduci da capo gli asset assegnati nell'Inspector.</summary>
    public void Play()
    {
        _cursor = 0;
        _audio.Stop();
        _audio.Play();
    }

    /// <summary>Alimenta audio + timeline a runtime (es. risposta di /tts) e riproduci.</summary>
    public void PlayRuntime(AudioClip newClip, string rhubarbJsonText)
    {
        _cursor = 0;
        LoadCues(rhubarbJsonText);
        _audio.clip = newClip;
        _audio.Play();
    }
}