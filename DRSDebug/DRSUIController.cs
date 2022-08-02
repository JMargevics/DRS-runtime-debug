using System.Collections;
using System.Collections.Generic;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.InputSystem;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine.EventSystems;

public class DRSUIController : MonoBehaviour
{
    //TODO save currentselected when collapsing
    //TODO refactor init UI state functions

    private DRSUIControls controls;
    private GameObject topDRSPanel, mainDRSPanel, DRSSettings, DLSSSettings;
    private Toggle enableDRSToggle, enableDLSSToggle, enableForcedToggle, useMipToggle, DLSSUseOptimal, enableFSRSharpness;
    private Image expandButton;
    private Slider lowResSlider, rayTracedHalfResSlider, minSlider, maxSlider, DLSSSharpness, forcedScreenPercentage, fsrSharpnessSlider;
    private TMP_Dropdown DRSType, filters, DLSSMode, DLSSInjectionPoint;

    private float[] posY = { 0, 0 }, sizeY = { 0, 0 };
    private bool expanded = true, DLSSExpanded, useOptimal;

    private HDRenderPipelineAsset HDRPAsset;
    private RenderPipelineSettings globalDRSSettings;
    private EventSystem es;
    private HDAdditionalCameraData cd;

    static Action<HDRenderPipelineAsset, RenderPipelineSettings> SetRenderPipelineSettings;
    private void Awake()
    {
        //Override main cam
        if (Camera.main.TryGetComponent<HDAdditionalCameraData>(out HDAdditionalCameraData _cd))
        {
            cd = _cd;
        } else
        {
            cd = Camera.main.gameObject.AddComponent<HDAdditionalCameraData>();
        }
        //Allow DRS and DLSS but disable custom overrides
        cd.allowDynamicResolution = true;
        cd.allowDeepLearningSuperSampling = true;
        cd.deepLearningSuperSamplingUseCustomAttributes = false;
        cd.deepLearningSuperSamplingUseCustomQualitySettings = false;

        es = this.transform.parent.GetComponentInChildren<EventSystem>();
        
        //quicker than standard reflection as it is compiled. thx Remi
        var field = typeof(HDRenderPipelineAsset).GetField("m_RenderPipelineSettings", BindingFlags.NonPublic | BindingFlags.Instance);
        var instance = Expression.Parameter(typeof(HDRenderPipelineAsset), "instance");
        var valueToUse = Expression.Parameter(typeof(RenderPipelineSettings), "valueToUse");
        var fieldExpression = Expression.Field(instance, field);
        var setter = Expression.Assign(fieldExpression, valueToUse);
        var lambda = Expression.Lambda<Action<HDRenderPipelineAsset, RenderPipelineSettings>>(setter, instance, valueToUse);
        SetRenderPipelineSettings = lambda.Compile();

        //Get the HDRP asset
        HDRPAsset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
        globalDRSSettings = HDRPAsset.currentPlatformRenderPipelineSettings;

        DLSSSettings = GameObject.Find("DLSSSettings");
        DRSSettings = GameObject.Find("DRSSettings");

        //Prepare UI refs
        topDRSPanel = GameObject.Find("TopDRSPanel");
        mainDRSPanel = GameObject.Find("MainDRSPanel");   
        expandButton = topDRSPanel.transform.Find("Expand").GetComponent<Image>();
        enableDRSToggle = mainDRSPanel.transform.Find("EnableDRSToggle").GetComponent<Toggle>();
        enableDLSSToggle = mainDRSPanel.transform.Find("EnableDLSSToggle").GetComponent<Toggle>();

        //DLSS elements refs
        DLSSMode = DLSSSettings.transform.Find("DLSSMode").GetComponent<TMP_Dropdown>();
        DLSSInjectionPoint = DLSSSettings.transform.Find("DLSSInjectionPoint").GetComponent<TMP_Dropdown>();
        DLSSUseOptimal = DLSSSettings.transform.Find("DLSSUseOptimal").GetComponent<Toggle>();
        DLSSSharpness = DLSSSettings.transform.Find("DLSSSharpness").GetComponent<Slider>();

        //DRS elements refs    
        enableForcedToggle = DRSSettings.transform.Find("ForcedScreenToggle").GetComponent<Toggle>();
        enableFSRSharpness = DRSSettings.transform.Find("Override FSR sharpness").GetComponent<Toggle>();
        useMipToggle = DRSSettings.transform.Find("UseMipToggle").GetComponent<Toggle>();
        filters = DRSSettings.transform.Find("Filters").GetComponent<TMP_Dropdown>();
        DRSType = DRSSettings.transform.Find("DRSType").GetComponent<TMP_Dropdown>();
        lowResSlider = DRSSettings.transform.Find("LowRes").GetComponent<Slider>();
        rayTracedHalfResSlider = DRSSettings.transform.Find("RayTraceHalfRes").GetComponent<Slider>();
        maxSlider = DRSSettings.transform.Find("Max %").GetComponent<Slider>();
        minSlider = DRSSettings.transform.Find("Min %").GetComponent<Slider>();
        forcedScreenPercentage = DRSSettings.transform.Find("Screen %").GetComponent<Slider>();
        fsrSharpnessSlider = DRSSettings.transform.Find("FSR sharpness").GetComponent<Slider>();

        //Compute Y positions and sizes of panels for expanding
        posY[0] = DRSSettings.GetComponent<RectTransform>().anchoredPosition.y;
        posY[1] = DRSSettings.GetComponent<RectTransform>().anchoredPosition.y - DLSSSettings.GetComponent<RectTransform>().sizeDelta.y;    
        sizeY[0] = mainDRSPanel.GetComponent<RectTransform>().sizeDelta.y;
        sizeY[1] = mainDRSPanel.GetComponent<RectTransform>().sizeDelta.y + DLSSSettings.GetComponent<RectTransform>().sizeDelta.y;

        //Listen for gamepad inputs
        controls = new DRSUIControls();
        controls.UI.Expand.performed += e => { expand(); } ;
        controls.UI.LBRB.performed += (value) => {
            if (es.currentSelectedGameObject.TryGetComponent<Slider>(out Slider _sl))
            {
                _sl.value += (float)(value.ReadValue<Vector2>().x * 5.0);
            }
        };

        initUI();
        
    }

    private void initUI()
    {  
        //Populate DLSS injection points
        if (DLSSInjectionPoint != null)
        {
            foreach (string f in System.Enum.GetNames(typeof(DynamicResolutionHandler.UpsamplerScheduleType)))
            {
                DLSSInjectionPoint.options.Add(new TMP_Dropdown.OptionData(f));
            }
        }

        //Populate filters
        //TODO Rename edge sharpening enum to FSR
        if (filters != null)
        {
            foreach (string f in System.Enum.GetNames(typeof(DynamicResUpscaleFilter)))
            {
                filters.options.Add(new TMP_Dropdown.OptionData(f));
            }
        }

        //Read states from our HDRP asset
        enableForcedToggle.isOn = globalDRSSettings.dynamicResolutionSettings.forceResolution;
        enableDRSToggle.isOn = globalDRSSettings.dynamicResolutionSettings.enabled;  
        enableDLSSToggle.isOn = globalDRSSettings.dynamicResolutionSettings.enableDLSS;
        filters.value = (int)globalDRSSettings.dynamicResolutionSettings.upsampleFilter;
        useMipToggle.isOn = globalDRSSettings.dynamicResolutionSettings.useMipBias;
        DRSType.value = (int)globalDRSSettings.dynamicResolutionSettings.dynResType;
        lowResSlider.value = globalDRSSettings.dynamicResolutionSettings.lowResTransparencyMinimumThreshold;
        rayTracedHalfResSlider.value = globalDRSSettings.dynamicResolutionSettings.rayTracingHalfResThreshold;
        minSlider.value = globalDRSSettings.dynamicResolutionSettings.minPercentage;
        maxSlider.value = globalDRSSettings.dynamicResolutionSettings.maxPercentage;
        forcedScreenPercentage.value = globalDRSSettings.dynamicResolutionSettings.forcedPercentage;

        DLSSMode.value = (int)globalDRSSettings.dynamicResolutionSettings.DLSSPerfQualitySetting;

        DLSSSharpness.value = globalDRSSettings.dynamicResolutionSettings.DLSSSharpness;
        DLSSUseOptimal.isOn = globalDRSSettings.dynamicResolutionSettings.DLSSUseOptimalSettings;

        Toggle[] toggles = mainDRSPanel.GetComponentsInChildren<Toggle>();
        if (toggles.Length > 0)
        {
            foreach (Toggle obj in toggles)
            {
                obj.onValueChanged.AddListener(f => { applyDRSSettings(); });
            }
        }

        TMP_Dropdown[] dropdowns = mainDRSPanel.GetComponentsInChildren<TMP_Dropdown>();
        if (dropdowns.Length > 0)
        {
            foreach (TMP_Dropdown obj in dropdowns)
            {
                obj.onValueChanged.AddListener(f => { applyDRSSettings(); });
            }
        }

        //update all slider labels, attach min/max clamper and get some instances
        Slider[] sliders = mainDRSPanel.GetComponentsInChildren<Slider>();
        if (sliders.Length > 0)
        {
            foreach (Slider sl in sliders)
            {
                updateSliderLabel(sl);
                sl.onValueChanged.AddListener(f => {applyDRSSettings(); });   
            }
        }

        //Update availability and states of whole UI
        expandDLSS();
        updateForced();
        updateFSRSharpness();
        StartCoroutine(updateDLSS());
        updateDRSAvailability();
    }

 
    public void applyDRSSettings()
    {
        globalDRSSettings.dynamicResolutionSettings.enabled = enableDRSToggle.isOn;
        globalDRSSettings.dynamicResolutionSettings.enableDLSS = enableDLSSToggle.isOn;
        globalDRSSettings.dynamicResolutionSettings.DLSSPerfQualitySetting = (uint)DLSSMode.value;
        globalDRSSettings.dynamicResolutionSettings.DLSSUseOptimalSettings = DLSSUseOptimal.isOn;
        globalDRSSettings.dynamicResolutionSettings.DLSSSharpness = DLSSSharpness.value;
        globalDRSSettings.dynamicResolutionSettings.upsampleFilter = (DynamicResUpscaleFilter)filters.value;
        globalDRSSettings.dynamicResolutionSettings.forceResolution = enableForcedToggle.isOn;
        globalDRSSettings.dynamicResolutionSettings.forcedPercentage = forcedScreenPercentage.value;
        globalDRSSettings.dynamicResolutionSettings.minPercentage = minSlider.value;
        globalDRSSettings.dynamicResolutionSettings.maxPercentage = maxSlider.value;
        globalDRSSettings.dynamicResolutionSettings.lowResTransparencyMinimumThreshold = lowResSlider.value;
        globalDRSSettings.dynamicResolutionSettings.rayTracingHalfResThreshold = rayTracedHalfResSlider.value;

        globalDRSSettings.dynamicResolutionSettings.useMipBias = useMipToggle.isOn;
        
        SetRenderPipelineSettings(HDRPAsset, globalDRSSettings);
           
    }

    private void OnEnable()
    {
        controls.Enable();
    }

    IEnumerator updateDLSS()
    {
        yield return new WaitForSeconds(0.1f); //wait till late render features are ready
        if (enableDLSSToggle != null && enableDRSToggle.isOn)
        {
            enableDLSSToggle.interactable = HDDynamicResolutionPlatformCapabilities.DLSSDetected;
            updateUseOptimal();
        }
        expandDLSS();
    }

    public void updateForced()
    {
        forcedScreenPercentage.interactable = enableForcedToggle.isOn;
        maxSlider.interactable = !enableForcedToggle.isOn;
        minSlider.interactable = !enableForcedToggle.isOn;
    }

    public void updateFSRSharpness()
    {
        enableFSRSharpness.interactable = filters.value == (int)DynamicResUpscaleFilter.EdgeAdaptiveScalingUpres;
        fsrSharpnessSlider.interactable = enableFSRSharpness.isOn && enableFSRSharpness.interactable;
    }

    public void updateDRSAvailability()
    {
        Selectable[] sel = mainDRSPanel.GetComponentsInChildren<Selectable>();
        if (sel.Length > 0)
        {
            foreach (Selectable sl in sel)
            {
                if (sl.name != "EnableDRSToggle")
                {
                    sl.interactable = enableDRSToggle.isOn;
                }
            }
        }
        
        if (enableDRSToggle.isOn)
        {
            updateForced();
            updateFSRSharpness();
            StartCoroutine(updateDLSS());    
        }
    }

    public void expand()
    {
        expandButton.transform.localScale *= -1;
        expanded = !expanded;
        mainDRSPanel.SetActive(expanded);

        if (expanded)
        {
            es.SetSelectedGameObject(es.firstSelectedGameObject); //TODO fix current selection as null
        }
    }
    
    public void updateUseOptimal()
    {
        useOptimal = DLSSUseOptimal.isOn;
        DLSSSharpness.interactable = !useOptimal;
    }
    
    public void expandDLSS()
    {
        DLSSExpanded = enableDLSSToggle.isOn && enableDLSSToggle.interactable; //TODO change second interactable
        DLSSSettings.SetActive(DLSSExpanded);

        if (DLSSExpanded)
        {
            mainDRSPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(mainDRSPanel.GetComponent<RectTransform>().sizeDelta.x,sizeY[1]);
            DRSSettings.GetComponent<RectTransform>().anchoredPosition = new Vector2(0,posY[1]);
        }
        else
        {
            mainDRSPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(mainDRSPanel.GetComponent<RectTransform>().sizeDelta.x,sizeY[0]);
            DRSSettings.GetComponent<RectTransform>().anchoredPosition = new Vector2(0,posY[0]);
        }
    }

    public void clampMinMax()
    {
        minSlider.value = Mathf.Clamp(minSlider.value, 0, maxSlider.value);
        maxSlider.value = Mathf.Clamp(maxSlider.value, minSlider.value, 100);
    }

    public void updateSliderLabel(Slider slider)
    {
        var valueLabel = slider.transform.Find("Value").GetComponent<Text>();
        if (valueLabel != null)
        {
            valueLabel.text = System.Math.Round(slider.value, 2).ToString();
        }  
    }   
}
