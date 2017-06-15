﻿using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Spectrum.Audio;
using XSerializer;
using System.IO;
using Spectrum.MIDI;
using System.Windows.Data;
using Xceed.Wpf.Toolkit;
using System.Collections.Generic;
using System.Windows.Media;
using Spectrum.Base;

namespace Spectrum {

  public partial class MainWindow : Window {

    private class MidiDeviceEntry {

      public int DeviceID { get; set; }

      public string DeviceName {
        get {
          if (MidiInput.DeviceCount <= this.DeviceID) {
            return "< DISCONNECTED >";
          }
          return MidiInput.GetDeviceName(this.DeviceID);
        }
      }

      public string PresetName { get; set; }

    }

    private Operator op;
    private SpectrumConfiguration config;
    private bool loadingConfig = false;
    private int[] audioDeviceIndices;
    private List<int> midiDeviceIndices;
    private List<int> midiPresetIndices;
    private DomeSimulatorWindow domeSimulatorWindow;
    private BarSimulatorWindow barSimulatorWindow;
    private StageSimulatorWindow stageSimulatorWindow;
    private int? currentlyEditing = null;

    public MainWindow() {
      this.InitializeComponent();

      new HotKey(Key.Q, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemTilde, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.R, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemPeriod, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemComma, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Left, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Right, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Up, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Down, KeyModifier.Alt, this.OnHotKeyHandler);

      this.LoadConfig();
    }

    private void HandleClose(object sender, EventArgs e) {
      this.op.Enabled = false;
      this.SaveConfig();
    }

    private void SaveConfig() {
      if (this.loadingConfig) {
        return;
      }
      // We keep around the old config in case the new config causes a crash
      if (File.Exists("spectrum_config.xml")) {
        File.Copy("spectrum_config.xml", "spectrum_old_config.xml", true);
      }
      using (
        FileStream stream = new FileStream(
          "spectrum_config.xml",
          FileMode.Create
        )
      ) {
        new XmlSerializer<SpectrumConfiguration>().Serialize(
          stream,
          this.config
        );
      }
    }

    private void UpdateConfig(
      object sender,
      DataTransferEventArgs eventArgs
    ) {
      if (this.config != null) {
        this.SaveConfig();
      }
    }

    private void UpdateConfigAndReboot(
      object sender,
      DataTransferEventArgs eventArgs
    ) {
      if (this.config != null) {
        this.op.Reboot();
        this.SaveConfig();
      }
    }

    private void LoadConfig() {
      this.loadingConfig = true;

      if (File.Exists("spectrum_config.xml")) {
        using (FileStream stream = File.OpenRead("spectrum_config.xml")) {
          this.config = new XmlSerializer<SpectrumConfiguration>(
          ).Deserialize(stream);
        }
      }
      if (this.config == null) {
        this.config = new SpectrumConfiguration();
      }
      this.op = new Operator(this.config);

      this.RefreshAudioDevices(null, null);
      this.RefreshLEDBoardPorts(null, null);
      this.RefreshMidiDevices(null, null);
      this.RefreshDomePorts(null, null);
      this.RefreshBarUSBPorts(null, null);
      this.RefreshStageUSBPorts(null, null);
      this.LoadPresets();
      this.LoadMidiDevices();

      this.Bind("huesEnabled", this.hueEnabled, CheckBox.IsCheckedProperty);
      this.Bind("ledBoardEnabled", this.ledBoardEnabled, CheckBox.IsCheckedProperty);
      this.Bind("midiInputEnabled", this.midiEnabled, CheckBox.IsCheckedProperty);
      this.Bind("audioInputInSeparateThread", this.audioThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("huesOutputInSeparateThread", this.hueThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("ledBoardOutputInSeparateThread", this.ledBoardThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("midiInputInSeparateThread", this.midiThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("domeOutputInSeparateThread", this.domeThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("whyFireOutputInSeparateThread", this.whyFireThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("barOutputInSeparateThread", this.barThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("stageOutputInSeparateThread", this.stageThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("operatorFPS", this.operatorFPSLabel, Label.ContentProperty);
      this.Bind("operatorFPS", this.operatorFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeHardwareSetup", this.domeHardwareSetup, ComboBox.SelectedItemProperty, false, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.fiveTeensies, [1] = this.beagleboneViaOPC, [2] = this.beagleboneViaCAMP }, true));
      this.Bind("domeHardwareSetup", this.domeTeensies, WrapPanel.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Visible, [1] = Visibility.Collapsed, [2] = Visibility.Collapsed }));
      this.Bind("domeHardwareSetup", this.domeBeagleboneOPCPanel, Grid.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Visible, [2] = Visibility.Collapsed }));
      this.Bind("domeHardwareSetup", this.domeBeagleboneCAMPPanel, Grid.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Collapsed, [2] = Visibility.Visible }));
      this.Bind("domeTeensyFPS1", this.domeTeensyFPS1Label, Label.ContentProperty);
      this.Bind("domeTeensyFPS1", this.domeTeensyFPS1Label, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensyFPS1Label, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensy1, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeTeensyFPS2", this.domeTeensyFPS2Label, Label.ContentProperty);
      this.Bind("domeTeensyFPS2", this.domeTeensyFPS2Label, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensyFPS2Label, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensy2, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeTeensyFPS3", this.domeTeensyFPS3Label, Label.ContentProperty);
      this.Bind("domeTeensyFPS3", this.domeTeensyFPS3Label, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensyFPS3Label, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensy3, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeTeensyFPS4", this.domeTeensyFPS4Label, Label.ContentProperty);
      this.Bind("domeTeensyFPS4", this.domeTeensyFPS4Label, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensyFPS4Label, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensy4, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeTeensyFPS5", this.domeTeensyFPS5Label, Label.ContentProperty);
      this.Bind("domeTeensyFPS5", this.domeTeensyFPS5Label, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensyFPS5Label, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensy5, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeBeagleboneOPCAddress", this.domeBeagleboneOPCHostAndPort, TextBox.TextProperty);
      this.Bind("domeBeagleboneOPCFPS", this.domeBeagleboneOPCFPSLabel, Label.ContentProperty);
      this.Bind("domeBeagleboneOPCFPS", this.domeBeagleboneOPCFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeBeagleboneOPCFPSLabel, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeBeagleboneOPCHostAndPort, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeBeagleboneCAMPAddress", this.domeBeagleboneCAMPHostAndPort, TextBox.TextProperty);
      this.Bind("domeBeagleboneCAMPFPS", this.domeBeagleboneCAMPFPSLabel, Label.ContentProperty);
      this.Bind("domeBeagleboneCAMPFPS", this.domeBeagleboneCAMPFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeBeagleboneCAMPFPSLabel, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeBeagleboneCAMPHostAndPort, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeTestPattern", this.domeTestPattern, ComboBox.SelectedItemProperty, false, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.domeTestPatternNone, [1] = this.domeTestPatternFlashColorsByStrut, [2] = this.domeTestPatternIterateThroughStruts }, true));
      this.Bind("boardTeensyFPS", this.boardTeensyFPSLabel, Label.ContentProperty);
      this.Bind("boardTeensyFPS", this.boardTeensyFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.boardTeensyFPSLabel, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.ledBoardUSBPorts, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("boardBeagleboneOPCAddress", this.boardBeagleboneOPCHostAndPort, TextBox.TextProperty);
      this.Bind("boardBeagleboneOPCFPS", this.boardBeagleboneOPCFPSLabel, Label.ContentProperty);
      this.Bind("boardBeagleboneOPCFPS", this.boardBeagleboneOPCFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.boardBeagleboneOPCFPSLabel, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.boardBeagleboneOPCHostAndPort, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("boardBeagleboneCAMPAddress", this.boardBeagleboneCAMPHostAndPort, TextBox.TextProperty);
      this.Bind("boardBeagleboneCAMPFPS", this.boardBeagleboneCAMPFPSLabel, Label.ContentProperty);
      this.Bind("boardBeagleboneCAMPFPS", this.boardBeagleboneCAMPFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.boardBeagleboneCAMPFPSLabel, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.boardBeagleboneCAMPHostAndPort, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("barTeensyFPS", this.barTeensyFPSLabel, Label.ContentProperty);
      this.Bind("barTeensyFPS", this.barTeensyFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("barOutputInSeparateThread", this.barTeensyFPSLabel, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("barOutputInSeparateThread", this.barUSBPorts, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("barBeagleboneOPCAddress", this.barBeagleboneOPCHostAndPort, TextBox.TextProperty);
      this.Bind("barBeagleboneOPCFPS", this.barBeagleboneOPCFPSLabel, Label.ContentProperty);
      this.Bind("barBeagleboneOPCFPS", this.barBeagleboneOPCFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("barOutputInSeparateThread", this.barBeagleboneOPCFPSLabel, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("barOutputInSeparateThread", this.barBeagleboneOPCHostAndPort, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("barBeagleboneCAMPAddress", this.barBeagleboneCAMPHostAndPort, TextBox.TextProperty);
      this.Bind("barBeagleboneCAMPFPS", this.barBeagleboneCAMPFPSLabel, Label.ContentProperty);
      this.Bind("barBeagleboneCAMPFPS", this.barBeagleboneCAMPFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("barOutputInSeparateThread", this.barBeagleboneCAMPFPSLabel, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("barOutputInSeparateThread", this.barBeagleboneCAMPHostAndPort, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("barTestPattern", this.barTestPattern, ComboBox.SelectedItemProperty, false, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.barTestPatternNone, [1] = this.barTestPatternFlashColors }, true));
      this.Bind("stageTeensyFPS1", this.stageTeensyFPS1Label, Label.ContentProperty);
      this.Bind("stageTeensyFPS1", this.stageTeensyFPS1Label, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("stageTeensyFPS2", this.stageTeensyFPS2Label, Label.ContentProperty);
      this.Bind("stageTeensyFPS2", this.stageTeensyFPS2Label, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("stageOutputInSeparateThread", this.stageTeensyFPS1Label, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("stageOutputInSeparateThread", this.stageTeensyUSBPorts1, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("stageOutputInSeparateThread", this.stageTeensyFPS2Label, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("stageOutputInSeparateThread", this.stageTeensyUSBPorts2, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("stageBeagleboneOPCAddress", this.stageBeagleboneOPCHostAndPort, TextBox.TextProperty);
      this.Bind("stageBeagleboneOPCFPS", this.stageBeagleboneOPCFPSLabel, Label.ContentProperty);
      this.Bind("stageBeagleboneOPCFPS", this.stageBeagleboneOPCFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("stageOutputInSeparateThread", this.stageBeagleboneOPCFPSLabel, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("stageOutputInSeparateThread", this.stageBeagleboneOPCHostAndPort, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("stageBeagleboneCAMPAddress", this.stageBeagleboneCAMPHostAndPort, TextBox.TextProperty);
      this.Bind("stageBeagleboneCAMPFPS", this.stageBeagleboneCAMPFPSLabel, Label.ContentProperty);
      this.Bind("stageBeagleboneCAMPFPS", this.stageBeagleboneCAMPFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("stageOutputInSeparateThread", this.stageBeagleboneCAMPFPSLabel, Label.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("stageOutputInSeparateThread", this.stageBeagleboneCAMPHostAndPort, ComboBox.WidthProperty, false, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("stageTestPattern", this.stageTestPattern, ComboBox.SelectedItemProperty, false, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.stageTestPatternNone, [1] = this.stageTestPatternFlashColors }, true));
      this.Bind("hueDelay", this.hueCommandDelay, TextBox.TextProperty);
      this.Bind("hueIdleOnSilent", this.hueIdleOnSilent, CheckBox.IsCheckedProperty);
      this.Bind("hueOverrideIndex", this.hueOverride, ComboBox.SelectedIndexProperty);
      this.Bind("hueOverrideIsCustom", this.hueCustomGrid, Grid.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("hueOverrideIsDisabled", this.hueIdleOnSilent, Grid.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("brighten", this.hueCustomBrightness, TextBox.TextProperty);
      this.Bind("sat", this.hueCustomSaturation, TextBox.TextProperty);
      this.Bind("colorslide", this.hueCustomHue, TextBox.TextProperty);
      this.Bind("peakC", this.peakChangeS, Slider.ValueProperty);
      this.Bind("peakC", this.peakChangeL, Label.ContentProperty);
      this.Bind("dropQ", this.dropQuietS, Slider.ValueProperty);
      this.Bind("dropQ", this.dropQuietL, Label.ContentProperty);
      this.Bind("dropT", this.dropChangeS, Slider.ValueProperty);
      this.Bind("dropT", this.dropChangeL, Label.ContentProperty);
      this.Bind("kickQ", this.kickQuietS, Slider.ValueProperty);
      this.Bind("kickQ", this.kickQuietL, Label.ContentProperty);
      this.Bind("kickT", this.kickChangeS, Slider.ValueProperty);
      this.Bind("kickT", this.kickChangeL, Label.ContentProperty);
      this.Bind("snareQ", this.snareQuietS, Slider.ValueProperty);
      this.Bind("snareQ", this.snareQuietL, Label.ContentProperty);
      this.Bind("snareT", this.snareChangeS, Slider.ValueProperty);
      this.Bind("snareT", this.snareChangeL, Label.ContentProperty);
      this.Bind("hueURL", this.hueHubAddress, TextBox.TextProperty);
      this.Bind("hueIndices", this.hueLightIndices, TextBox.TextProperty, false, BindingMode.TwoWay, new StringJoinConverter());
      this.Bind("boardRowLength", this.ledBoardRowLength, TextBox.TextProperty);
      this.Bind("boardRowsPerStrip", this.ledBoardRowsPerStrip, TextBox.TextProperty);
      this.Bind("boardBrightness", this.ledBoardBrightnessSlider, Slider.ValueProperty);
      this.Bind("boardBrightness", this.ledBoardBrightnessLabel, Label.ContentProperty);
      this.Bind("boardHardwareSetup", this.boardHardwareSetup, ComboBox.SelectedItemProperty, false, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.boardHardwareSetupTeensy, [1] = this.boardHardwareSetupBeagleboneViaOPC, [2] = this.boardHardwareSetupBeagleboneViaCAMP }, true));
      this.Bind("boardHardwareSetup", this.boardTeensyPanel, WrapPanel.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Visible, [1] = Visibility.Collapsed, [2] = Visibility.Collapsed }));
      this.Bind("boardHardwareSetup", this.boardBeagleboneOPCPanel, Grid.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Visible, [2] = Visibility.Collapsed }));
      this.Bind("boardHardwareSetup", this.boardBeagleboneCAMPPanel, Grid.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Collapsed, [2] = Visibility.Visible }));
      this.Bind("domeEnabled", this.domeEnabled, CheckBox.IsCheckedProperty);
      this.Bind("domeSimulationEnabled", this.domeSimulationEnabled, CheckBox.IsCheckedProperty);
      this.Bind("domeMaxBrightness", this.domeMaxBrightnessSlider, Slider.ValueProperty);
      this.Bind("domeMaxBrightness", this.domeMaxBrightnessLabel, Label.ContentProperty);
      this.Bind("domeBrightness", this.domeBrightnessSlider, Slider.ValueProperty);
      this.Bind("domeBrightness", this.domeBrightnessLabel, Label.ContentProperty);
      this.Bind("domeVolumeAnimationSize", this.domeVolumeAnimationSize, ComboBox.SelectedIndexProperty);
      this.Bind("domeAutoFlashDelay", this.domeAutoFlashDelay, TextBox.TextProperty);
      this.Bind("domeSkipLEDs", this.domeSkipLEDs, TextBox.TextProperty);
      var colorConverter = new ColorConverter();
      this.Bind("[0,0]", this.color0_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[0,1]", this.color0_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[0]", this.domeCC0, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[1,0]", this.color1_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[1,1]", this.color1_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[1]", this.domeCC1, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[2,0]", this.color2_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[2,1]", this.color2_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[2]", this.domeCC2, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[3,0]", this.color3_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[3,1]", this.color3_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[3]", this.domeCC3, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[4,0]", this.color4_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[4,1]", this.color4_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[4]", this.domeCC4, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[5,0]", this.color5_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[5,1]", this.color5_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[5]", this.domeCC5, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[6,0]", this.color6_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[6,1]", this.color6_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[6]", this.domeCC6, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[7,0]", this.color7_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[7,1]", this.color7_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[7]", this.domeCC7, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[8,0]", this.color8_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[8,1]", this.color8_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[8]", this.domeCC8, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[9,0]", this.color9_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[9,1]", this.color9_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[9]", this.domeCC9, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[10,0]", this.color10_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[10,1]", this.color10_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[10]", this.domeCC10, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[11,0]", this.color11_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[11,1]", this.color11_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[11]", this.domeCC11, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[12,0]", this.color12_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[12,1]", this.color12_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[12]", this.domeCC12, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[13,0]", this.color13_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[13,1]", this.color13_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[13]", this.domeCC13, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[14,0]", this.color14_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[14,1]", this.color14_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[14]", this.domeCC14, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[15,0]", this.color15_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[15,1]", this.color15_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[15]", this.domeCC15, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("whyFireEnabled", this.whyFireEnabled, CheckBox.IsCheckedProperty);
      this.Bind("whyFireURL", this.whyFireAddress, TextBox.TextProperty);
      this.Bind("barEnabled", this.barEnabled, CheckBox.IsCheckedProperty);
      this.Bind("barSimulationEnabled", this.barSimulationEnabled, CheckBox.IsCheckedProperty);
      this.Bind("barHardwareSetup", this.barHardwareSetup, ComboBox.SelectedItemProperty, false, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.barHardwareSetupTeensy, [1] = this.barHardwareSetupBeagleboneViaOPC, [2] = this.barHardwareSetupBeagleboneViaCAMP }, true));
      this.Bind("barHardwareSetup", this.barTeensyPanel, WrapPanel.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Visible, [1] = Visibility.Collapsed, [2] = Visibility.Collapsed }));
      this.Bind("barHardwareSetup", this.barBeagleboneOPCPanel, Grid.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Visible, [2] = Visibility.Collapsed }));
      this.Bind("barHardwareSetup", this.barBeagleboneCAMPPanel, Grid.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Collapsed, [2] = Visibility.Visible }));
      this.Bind("barInfinityLength", this.barInfinityLength, TextBox.TextProperty);
      this.Bind("barInfinityWidth", this.barInfiniteWidth, TextBox.TextProperty);
      this.Bind("barRunnerLength", this.barRunnerLength, TextBox.TextProperty);
      this.Bind("barBrightness", this.barBrightnessSlider, Slider.ValueProperty);
      this.Bind("barBrightness", this.barBrightnessLabel, Label.ContentProperty);
      this.Bind("stageEnabled", this.stageEnabled, CheckBox.IsCheckedProperty);
      this.Bind("stageSimulationEnabled", this.stageSimulationEnabled, CheckBox.IsCheckedProperty);
      this.Bind("stageHardwareSetup", this.stageHardwareSetup, ComboBox.SelectedItemProperty, false, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.stageHardwareSetupTwoTeensies, [1] = this.stageHardwareSetupBeagleboneViaOPC, [2] = this.stageHardwareSetupBeagleboneViaCAMP }, true));
      this.Bind("stageHardwareSetup", this.stageTeensyPanel, WrapPanel.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Visible, [1] = Visibility.Collapsed, [2] = Visibility.Collapsed }));
      this.Bind("stageHardwareSetup", this.stageBeagleboneOPCPanel, Grid.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Visible, [2] = Visibility.Collapsed }));
      this.Bind("stageHardwareSetup", this.stageBeagleboneCAMPPanel, Grid.VisibilityProperty, false, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Collapsed, [2] = Visibility.Visible }));
      this.Bind("stageSideLengths", this.stageSideLengths, TextBox.TextProperty, false, BindingMode.TwoWay, new StringJoinConverter());
      this.Bind("stageBrightness", this.stageBrightnessSlider, Slider.ValueProperty);
      this.Bind("stageBrightness", this.stageBrightnessLabel, Label.ContentProperty);

      this.loadingConfig = false;
    }

    private void Bind(
      string configPath,
      FrameworkElement element,
      DependencyProperty property,
      bool rebootOnUpdate = false,
      BindingMode mode = BindingMode.TwoWay,
      IValueConverter converter = null,
      object source = null
    ) {
      var binding = new Binding(configPath);
      binding.Source = source != null ? source : this.config;
      binding.Mode = mode;
      if (converter != null) {
        binding.Converter = converter;
      }
      binding.NotifyOnSourceUpdated = true;
      if (rebootOnUpdate) {
        Binding.AddSourceUpdatedHandler(element, UpdateConfigAndReboot);
      } else {
        Binding.AddSourceUpdatedHandler(element, UpdateConfig);
      }
      element.SetBinding(property, binding);
    }

    private void SliderStarted(object sender, DragStartedEventArgs e) {
      this.loadingConfig = true;
    }

    private void SliderCompleted(object sender, DragCompletedEventArgs e) {
      this.loadingConfig = false;
      this.SaveConfig();
    }

    private void OnHotKeyHandler(HotKey hotKey) {
      if (hotKey.Key.Equals(Key.Q)) {
        this.hueOverride.SelectedItem =
          this.hueOverride.SelectedItem == this.hueOverrideOn
            ? this.hueOverrideDisable
            : this.hueOverrideOn;
      } else if (hotKey.Key.Equals(Key.OemTilde)) {
        this.hueOverride.SelectedItem =
          this.hueOverride.SelectedItem == this.hueOverrideOff
            ? this.hueOverrideDisable
            : this.hueOverrideOff;
      } else if (hotKey.Key.Equals(Key.R)) {
        this.hueOverride.SelectedItem =
          this.hueOverride.SelectedItem == this.hueOverrideRed
            ? this.hueOverrideDisable
            : this.hueOverrideRed;
      } else if (hotKey.Key.Equals(Key.OemPeriod)) {
        this.hueCustomBrightness.Text =
          Math.Min(this.config.brighten + 1, 0).ToString();
      } else if (hotKey.Key.Equals(Key.OemComma)) {
        this.hueCustomBrightness.Text =
          Math.Max(this.config.brighten - 1, -4).ToString();
      } else if (hotKey.Key.Equals(Key.Left)) {
        this.hueCustomHue.Text = (this.config.colorslide - 1).ToString();
      } else if (hotKey.Key.Equals(Key.Right)) {
        this.hueCustomHue.Text = (this.config.colorslide + 1).ToString();
      } else if (hotKey.Key.Equals(Key.Up)) {
        this.hueCustomSaturation.Text =
          Math.Min(this.config.sat + 1, 2).ToString();
      } else if (hotKey.Key.Equals(Key.Down)) {
        this.hueCustomSaturation.Text =
          Math.Max(this.config.sat - 1, -2).ToString();
      }
      //config.colorslide = (config.colorslide + 4 + 16) % 16 - 4;??
    }

    private void PowerButtonClicked(object sender, RoutedEventArgs e) {
      if (this.op.Enabled) {
        this.op.Enabled = false;
        this.powerButton.Content = "Go";
      } else {
        this.op.Enabled = true;
        this.powerButton.Content = "Stop";
      }
    }

    private void RefreshAudioDevices(object sender, RoutedEventArgs e) {
      this.op.Enabled = false;
      this.powerButton.Content = "Go";

      int deviceCount = AudioInput.DeviceCount;
      this.audioDeviceIndices = new int[deviceCount];

      this.audioDevices.Items.Clear();
      int itemIndex = 0;
      for (int i = 0; i < deviceCount; i++) {
        if (!AudioInput.IsEnabledInputDevice(i)) {
          continue;
        }
        this.audioDevices.Items.Add(AudioInput.GetDeviceName(i));
        this.audioDeviceIndices[itemIndex++] = i;
      }

      this.audioDevices.SelectedIndex = Array.FindIndex(
        this.audioDeviceIndices,
        i => i == this.config.audioDeviceIndex
      );
    }

    private void AudioInputDeviceChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.audioDevices.SelectedIndex == -1) {
        return;
      }
      this.config.audioDeviceIndex =
        this.audioDeviceIndices[this.audioDevices.SelectedIndex];
      this.op.Reboot();
      this.SaveConfig();
    }

    private void RefreshLEDBoardPorts(object sender, RoutedEventArgs e) {
      this.ledBoardEnabled.IsChecked = false;

      this.ledBoardUSBPorts.Items.Clear();
      foreach (string portName in System.IO.Ports.SerialPort.GetPortNames()) {
        this.ledBoardUSBPorts.Items.Add(portName);
      }

      this.ledBoardUSBPorts.SelectedValue = this.config.boardTeensyUSBPort;
    }

    private void LEDBoardUSBPortsChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.ledBoardUSBPorts.SelectedIndex == -1) {
        return;
      }
      this.config.boardTeensyUSBPort = this.ledBoardUSBPorts.SelectedItem as string;
      this.op.Reboot();
      this.SaveConfig();
    }

    // Refresh the list of available devices for the "Add device" panel
    private void RefreshMidiDevices(object sender, RoutedEventArgs e) {
      var currentDevice = this.midiDevices.SelectedItem;

      this.midiDevices.Items.Clear();
      this.midiDeviceIndices = new List<int>();
      for (int i = 0; i < MidiInput.DeviceCount; i++) {
        if (!this.config.midiDevices.ContainsKey(i)) {
          this.midiDevices.Items.Add(MidiInput.GetDeviceName(i));
          this.midiDeviceIndices.Add(i);
        }
      }

      this.midiDevices.SelectedItem = currentDevice;
    }

    // Refresh the list of active/created "devices" (paired with a preset)
    private void LoadMidiDevices() {
      foreach (var pair in this.config.midiDevices) {
        this.midiDeviceList.Items.Add(new MidiDeviceEntry {
          DeviceID = pair.Key,
          PresetName = this.config.midiPresets[pair.Value].Name,
        });
      }
    }

    // Only called once, at the start, to populate the parts of the UI that
    // need a list of all presets
    private void LoadPresets() {
      this.midiNewDevicePreset.Items.Clear();
      this.midiPresetIndices = new List<int>();
      foreach (var pair in this.config.midiPresets) {
        var midiPreset = pair.Value;
        this.midiNewDevicePreset.Items.Add(midiPreset.Name);
        this.midiPresetIndices.Add(midiPreset.id);
        this.midiPresetList.Items.Add(midiPreset.Name);
      }
      this.midiNewDevicePreset.Items.Add("New preset");
    }

    private void MidiNewDeviceSelectionChanged(object sender, RoutedEventArgs e) {
      var lastVisibility = this.midiNewDevicePresetNameGrid.Visibility;
      this.midiNewDevicePresetNameGrid.Visibility =
        this.midiNewDevicePreset.SelectedIndex == this.midiPresetIndices.Count
          ? Visibility.Visible
          : Visibility.Collapsed;
      if (
        this.midiNewDevicePresetNameGrid.Visibility != lastVisibility &&
        this.midiNewDevicePresetNameGrid.Visibility == Visibility.Visible
      ) {
        this.midiNewDevicePresetName.Focus();
      }
    }

    private void MidiNewDeviceNewPresetNameLostFocus(object sender, RoutedEventArgs e) {
      var name = this.midiNewDevicePresetName.Text.Trim();
      if (String.IsNullOrEmpty(name)) {
        this.ClearMidiNewDevicePresetName();
      }
    }

    private void MidiNewDeviceNewPresetNameGotFocus(object sender, RoutedEventArgs e) {
      var name = this.midiNewDevicePresetName.Text.Trim();
      if (String.Equals(name, "New preset name")) {
        this.midiNewDevicePresetName.Text = "";
        this.midiNewDevicePresetName.Foreground = new SolidColorBrush(Colors.Black);
        this.midiNewDevicePresetName.FontStyle = FontStyles.Normal;
      }
    }

    private void ClearMidiNewDevicePresetName() {
      this.midiNewDevicePresetName.Text = "New preset name";
      this.midiNewDevicePresetName.Foreground = new SolidColorBrush(Colors.Gray);
      this.midiNewDevicePresetName.FontStyle = FontStyles.Italic;
    }

    private string ValidateNewMidiPresetName(string presetName) {
      var newPresetName = presetName.Trim();
      if (
        String.IsNullOrEmpty(newPresetName) ||
        String.Equals(newPresetName, "New preset name") ||
        this.MidiPresetNameExists(newPresetName)
      ) {
        return null;
      }
      return newPresetName;
    }

    private MidiPreset AddNewMidiPresetWithName(string presetName) {
      var newPresetName = this.ValidateNewMidiPresetName(presetName);
      if (newPresetName == null) {
        return null;
      }
      int newID = this.getNextMidiPresetID();
      var newPreset = new MidiPreset() { id = newID, Name = newPresetName };
      this.AddMidiPreset(newPreset);
      return newPreset;
    }

    private void AddMidiPreset(MidiPreset preset) {
      var newMidiPresets = new Dictionary<int, MidiPreset>(this.config.midiPresets);
      newMidiPresets[preset.id] = preset;
      this.config.midiPresets = newMidiPresets;
      this.midiNewDevicePreset.Items.Insert(
        this.midiNewDevicePreset.Items.Count - 1,
        preset.Name
      );
      this.midiPresetIndices.Add(preset.id);

      this.midiPresetList.Items.Add(preset.Name);
    }

    private int getNextMidiPresetID() {
      int newID = -1;
      foreach (var pair in this.config.midiPresets) {
        if (pair.Key > newID) {
          newID = pair.Key;
        }
      }
      return newID + 1;
    }

    private void MidiAddDeviceClicked(object sender, RoutedEventArgs e) {
      if (this.midiNewDevicePreset.SelectedIndex == -1) {
        this.midiNewDevicePreset.Focus();
        return;
      }
      if (this.midiDevices.SelectedIndex == -1) {
        this.midiDevices.Focus();
        return;
      }
      int presetID;
      string presetName;
      if (this.midiNewDevicePreset.SelectedIndex >= this.midiPresetIndices.Count) {
        // "New preset" was selected
        var result = this.AddNewMidiPresetWithName(this.midiNewDevicePresetName.Text);
        if (result == null) {
          this.midiNewDevicePresetName.Focus();
        }
        presetID = result.id;
        presetName = result.Name;
      } else {
        presetID = this.midiPresetIndices[this.midiNewDevicePreset.SelectedIndex];
        presetName = this.config.midiPresets[presetID].Name;
      }
      var deviceID = this.midiDeviceIndices[this.midiDevices.SelectedIndex];
      this.midiDeviceList.Items.Add(new MidiDeviceEntry {
        DeviceID = deviceID,
        PresetName = presetName,
      });
      var newDevices = new Dictionary<int, int>(this.config.midiDevices);
      newDevices.Add(deviceID, presetID);
      this.config.midiDevices = newDevices;
      this.midiDeviceIndices.RemoveAt(this.midiDevices.SelectedIndex);
      this.midiDevices.Items.RemoveAt(this.midiDevices.SelectedIndex);
      this.midiDevices.SelectedIndex = -1;
      this.midiNewDevicePreset.SelectedIndex = -1;
      this.ClearMidiNewDevicePresetName();
      this.midiNewDevicePresetNameGrid.Visibility = Visibility.Collapsed;
      this.SaveConfig();

      if (this.midiPresetList.SelectedIndex >= 0) {
        var currentPresetIndex = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
        if (presetID == currentPresetIndex) {
          this.midiDeletePreset.IsEnabled = false;
        }
      }
    }

    // Delete one of the active/created "devices"
    private void MidiDeleteDeviceClicked(object sender, RoutedEventArgs e) {
      var newDevices = new Dictionary<int, int>(this.config.midiDevices);
      MidiDeviceEntry item = (MidiDeviceEntry)this.midiDeviceList.SelectedItem;
      var presetIndex = newDevices[item.DeviceID];
      newDevices.Remove(item.DeviceID);
      this.config.midiDevices = newDevices;
      this.midiDeviceList.Items.RemoveAt(this.midiDeviceList.SelectedIndex);
      this.RefreshMidiDevices(null, null);
      this.SaveConfig();

      if (this.midiPresetList.SelectedIndex >= 0) {
        var currentPresetIndex = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
        if (
          presetIndex == currentPresetIndex &&
          !this.config.midiDevices.ContainsValue(currentPresetIndex)
        ) {
          this.midiDeletePreset.IsEnabled = true;
        }
      }
    }

    // Take a selected active/created "device" and load its preset into the preset panel
    private void MidiLoadPresetClicked(object sender, RoutedEventArgs e) {
      MidiDeviceEntry item = (MidiDeviceEntry)this.midiDeviceList.SelectedItem;
      this.midiPresetList.SelectedItem = item.PresetName;
      // TODO should eventually switch what's visible in the bottom panel
    }

    private void MidiDeviceListSelectionChanged(object sender, SelectionChangedEventArgs e) {
      var deviceSelected = this.midiDeviceList.SelectedIndex >= 0;
      this.midiDeleteDevice.IsEnabled = deviceSelected;
      this.midiLoadDevicePreset.IsEnabled = deviceSelected;
    }

    private void MidiAddPresetClicked(object sender, RoutedEventArgs e) {
      if (this.currentlyEditing.HasValue) {
        var newPresetName = this.ValidateNewMidiPresetName(this.midiNewPresetName.Text);
        if (newPresetName == null) {
          this.midiNewPresetName.Focus();
          return;
        }
        // We don't need to reset the whole midiPresets var to trigger observers since nobody
        // cares what presets are named
        this.config.midiPresets[this.currentlyEditing.Value].Name = newPresetName;
        var presetIndex = this.midiPresetIndices[this.currentlyEditing.Value];
        this.midiPresetList.Items[presetIndex] = newPresetName;
        this.midiNewDevicePreset.Items[presetIndex] = newPresetName;
        this.MidiCancelEditPresetClicked(null, null);
      } else {
        var result = this.AddNewMidiPresetWithName(this.midiNewPresetName.Text);
        if (result == null) {
          this.midiNewPresetName.Focus();
          return;
        }
      }
      this.ClearMidiNewPresetName();
      this.SaveConfig();
    }

    private void MidiDeletePresetClicked(object sender, RoutedEventArgs e) {
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      var newPresets = new Dictionary<int, MidiPreset>(this.config.midiPresets);
      newPresets.Remove(presetID);
      this.config.midiPresets = newPresets;
      this.midiPresetIndices.RemoveAt(this.midiPresetList.SelectedIndex);
      this.midiNewDevicePreset.Items.RemoveAt(this.midiPresetList.SelectedIndex);
      this.midiPresetList.Items.RemoveAt(this.midiPresetList.SelectedIndex);
      this.SaveConfig();
    }

    private void MidiPresetListSelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (this.midiPresetList.SelectedIndex < 0) {
        this.midiDeletePreset.IsEnabled = false;
        this.midiClonePreset.IsEnabled = false;
        this.midiRenamePreset.IsEnabled = false;
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      this.midiDeletePreset.IsEnabled = !this.config.midiDevices.ContainsValue(presetID);
      this.midiClonePreset.IsEnabled = true;
      this.midiRenamePreset.IsEnabled = true;
    }

    private bool MidiPresetNameExists(string name) {
      foreach (var pair in this.config.midiPresets) {
        if (pair.Value.Name == name) {
          return true;
        }
      }
      return false;
    }

    private void MidiClonePresetClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex < 0) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      MidiPreset clonedPreset = (MidiPreset)this.config.midiPresets[presetID].Clone();
      clonedPreset.id = this.getNextMidiPresetID();

      string newName = clonedPreset.Name + " (clone)";
      int i = 1;
      while (true) {
        if (!this.MidiPresetNameExists(newName)) {
          break;
        }
        newName = clonedPreset.Name + " (clone " + ++i + ")";
      }
      clonedPreset.Name = newName;

      this.AddMidiPreset(clonedPreset);
    }

    private void MidiRenamePresetClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex < 0) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      this.currentlyEditing = presetID;
      this.midiPresetEditLabel.Content = "Rename preset";
      this.midiNewPresetName.Width = 120;
      this.midiAddPreset.Content = "Save";
      this.midiAddPreset.Margin = new Thickness(0, 0, 55, 0);
      this.midiCancelEditPreset.Visibility = Visibility.Visible;
      this.midiNewPresetName.Focus();
    }

    private void MidiCancelEditPresetClicked(object sender, RoutedEventArgs e) {
      if (!this.currentlyEditing.HasValue) {
        return;
      }
      this.currentlyEditing = null;
      this.midiPresetEditLabel.Content = "Add preset";
      this.midiNewPresetName.Width = 140;
      this.midiAddPreset.Content = "Add preset";
      this.midiAddPreset.Margin = new Thickness(0, 0, 0, 0);
      this.midiCancelEditPreset.Visibility = Visibility.Collapsed;
      this.ClearMidiNewPresetName();
    }

    private void MidiNewPresetNameLostFocus(object sender, RoutedEventArgs e) {
      var name = this.midiNewPresetName.Text.Trim();
      if (String.IsNullOrEmpty(name)) {
        this.ClearMidiNewPresetName();
      }
    }

    private void MidiNewPresetNameGotFocus(object sender, RoutedEventArgs e) {
      var name = this.midiNewPresetName.Text.Trim();
      if (String.Equals(name, "New preset name")) {
        this.midiNewPresetName.Text = "";
        this.midiNewPresetName.Foreground = new SolidColorBrush(Colors.Black);
        this.midiNewPresetName.FontStyle = FontStyles.Normal;
      }
    }

    private void ClearMidiNewPresetName() {
      this.midiNewPresetName.Text = "New preset name";
      this.midiNewPresetName.Foreground = new SolidColorBrush(Colors.Gray);
      this.midiNewPresetName.FontStyle = FontStyles.Italic;
    }

    private void MidiBindingTypeSelectionChanged(object sender, SelectionChangedEventArgs e) {
    }

    private void MidiAddBindingClicked(object sender, RoutedEventArgs e) {
    }

    private void MidiDeleteBindingClicked(object sender, RoutedEventArgs e) {
    }

    private void RefreshDomePorts(object sender, RoutedEventArgs e) {
      this.domeEnabled.IsChecked = false;

      this.domeTeensy1.Items.Clear();
      this.domeTeensy2.Items.Clear();
      this.domeTeensy3.Items.Clear();
      this.domeTeensy4.Items.Clear();
      this.domeTeensy5.Items.Clear();
      foreach (string portName in System.IO.Ports.SerialPort.GetPortNames()) {
        this.domeTeensy1.Items.Add(portName);
        this.domeTeensy2.Items.Add(portName);
        this.domeTeensy3.Items.Add(portName);
        this.domeTeensy4.Items.Add(portName);
        this.domeTeensy5.Items.Add(portName);
      }

      this.domeTeensy1.SelectedValue = this.config.domeTeensyUSBPort1;
      this.domeTeensy2.SelectedValue = this.config.domeTeensyUSBPort2;
      this.domeTeensy3.SelectedValue = this.config.domeTeensyUSBPort3;
      this.domeTeensy4.SelectedValue = this.config.domeTeensyUSBPort4;
      this.domeTeensy5.SelectedValue = this.config.domeTeensyUSBPort5;
    }

    private void DomePortChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.domeTeensy1.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort1 = this.domeTeensy1.SelectedItem as string;
      }
      if (this.domeTeensy2.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort2 = this.domeTeensy2.SelectedItem as string;
      }
      if (this.domeTeensy3.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort3 = this.domeTeensy3.SelectedItem as string;
      }
      if (this.domeTeensy4.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort4 = this.domeTeensy4.SelectedItem as string;
      }
      if (this.domeTeensy5.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort5 = this.domeTeensy5.SelectedItem as string;
      }
      this.op.Reboot();
      this.SaveConfig();
    }

    private void OpenDomeSimulator(object sender, RoutedEventArgs e) {
      this.domeSimulatorWindow = new DomeSimulatorWindow(this.config);
      this.domeSimulatorWindow.Closed += DomeSimulatorClosed;
      this.domeSimulatorWindow.Show();
    }

    private void CloseDomeSimulator(object sender, RoutedEventArgs e) {
      this.domeSimulatorWindow.Close();
      this.domeSimulatorWindow = null;
    }

    private void DomeSimulatorClosed(object sender, EventArgs e) {
      this.config.domeSimulationEnabled = false;
    }

    private void OpenBarSimulator(object sender, RoutedEventArgs e) {
      this.barSimulatorWindow = new BarSimulatorWindow(this.config);
      this.barSimulatorWindow.Closed += BarSimulatorClosed;
      this.barSimulatorWindow.Show();
    }

    private void CloseBarSimulator(object sender, RoutedEventArgs e) {
      this.barSimulatorWindow.Close();
      this.barSimulatorWindow = null;
    }

    private void BarSimulatorClosed(object sender, EventArgs e) {
      this.config.barSimulationEnabled = false;
    }

    private void RefreshBarUSBPorts(object sender, RoutedEventArgs e) {
      this.barEnabled.IsChecked = false;

      this.barUSBPorts.Items.Clear();
      foreach (string portName in System.IO.Ports.SerialPort.GetPortNames()) {
        this.barUSBPorts.Items.Add(portName);
      }

      this.barUSBPorts.SelectedValue = this.config.barTeensyUSBPort;
    }

    private void BarUSBPortsChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.barUSBPorts.SelectedIndex == -1) {
        return;
      }
      this.config.barTeensyUSBPort = this.barUSBPorts.SelectedItem as string;
      this.op.Reboot();
      this.SaveConfig();
    }

    private void OpenStageSimulator(object sender, RoutedEventArgs e) {
      this.stageSimulatorWindow = new StageSimulatorWindow(this.config);
      this.stageSimulatorWindow.Closed += StageSimulatorClosed;
      this.stageSimulatorWindow.Show();
    }

    private void CloseStageSimulator(object sender, RoutedEventArgs e) {
      this.stageSimulatorWindow.Close();
      this.stageSimulatorWindow = null;
    }

    private void StageSimulatorClosed(object sender, EventArgs e) {
      this.config.stageSimulationEnabled = false;
    }

    private void RefreshStageUSBPorts(object sender, RoutedEventArgs e) {
      this.stageEnabled.IsChecked = false;

      this.stageTeensyUSBPorts1.Items.Clear();
      this.stageTeensyUSBPorts2.Items.Clear();
      foreach (string portName in System.IO.Ports.SerialPort.GetPortNames()) {
        this.stageTeensyUSBPorts1.Items.Add(portName);
        this.stageTeensyUSBPorts2.Items.Add(portName);
      }

      this.stageTeensyUSBPorts1.SelectedValue = this.config.stageTeensyUSBPort1;
      this.stageTeensyUSBPorts2.SelectedValue = this.config.stageTeensyUSBPort1;
    }

    private void StageUSBPortsChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.stageTeensyUSBPorts1.SelectedIndex != -1) {
        this.config.stageTeensyUSBPort1 = this.stageTeensyUSBPorts1.SelectedItem as string;
      }
      if (this.stageTeensyUSBPorts2.SelectedIndex != -1) {
        this.config.stageTeensyUSBPort2 = this.stageTeensyUSBPorts2.SelectedItem as string;
      }
      this.op.Reboot();
      this.SaveConfig();
    }

  }

}