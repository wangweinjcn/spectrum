﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sanford.Multimedia.Midi;
using Spectrum.Base;
using System.Threading;
using System.Collections.Concurrent;

namespace Spectrum.MIDI {

  using BindingKey = Tuple<MidiCommandType, int>;

  public enum MidiCommandType : byte { Knob, Note, Program }

  public struct MidiCommand {
    public MidiCommandType type;
    public int index;
    public double value;
  }

  struct Binding {
    public BindingKey key;
    public delegate void bindingCallback(int index, double val);
    public bindingCallback callback;
  }

  public class MidiInput : Input {

    // Each key on the keyboard corresponds to a color
    private static int[] colorFromColorIndex = new int[] {
      0x000000, 0xFF0000, 0xFF4400, 0xFF8800, 0xFFCC00, 0xFFFF00, 0xCCFF00,
      0x88FF00, 0x44FF00, 0x00FF00, 0x00FF44, 0x00FF88, 0x00FFCC, 0x00FFFF,
      0x00CCFF, 0x0088FF, 0x0044FF, 0x0000FF, 0x4400FF, 0x8800FF, 0xCC00FF,
      0xFF00FF, 0xFF55FF, 0xFFABFF, 0xFFFFFF,
    };

    private Configuration config;
    private InputDevice device;
    private ConcurrentQueue<MidiCommand> buffer;
    private Dictionary<int, double> knobValues;
    private Dictionary<int, double> noteVelocities;
    private int activeProg;
    private int curFirstColorIndex;
    private MidiCommand[] commandsSinceLastTick;
    private Dictionary<BindingKey, List<Binding>> bindings;

    public MidiInput(Configuration config) {
      this.config = config;
      this.buffer = new ConcurrentQueue<MidiCommand>();
      this.knobValues = new Dictionary<int, double>();
      this.noteVelocities = new Dictionary<int, double>();
      this.activeProg = -1;
      this.curFirstColorIndex = -1;
      this.commandsSinceLastTick = new MidiCommand[0];
      this.SetBindings();
    }

    private void SetBindings() {
      this.bindings = new Dictionary<BindingKey, List<Binding>>();

      // Basic state
      this.AddBinding(MidiCommandType.Note, (index, val) => this.noteVelocities[index] = val);
      this.AddBinding(MidiCommandType.Knob, (index, val) => this.knobValues[index] = val);

      // Color palette logic
      this.AddBinding(MidiCommandType.Program, (index, val) => this.activeProg = index);
      this.AddBinding(MidiCommandType.Knob, (index, val) => {
        this.activeProg = -1;
        this.curFirstColorIndex = -1;
      });
      this.AddBinding(MidiCommandType.Note, (index, val) => {
        if (this.activeProg == -1) {
          return;
        }
        if (index < 64 || index >= 89) {
          this.activeProg = -1;
          this.curFirstColorIndex = -1;
          return;
        }
        int colorIndex = index - 64;
        if (val == 0) {
          if (colorIndex == this.curFirstColorIndex) {
            this.curFirstColorIndex = -1;
          }
          return;
        }
        if (this.curFirstColorIndex == -1) {
          this.curFirstColorIndex = colorIndex;
          this.config.domeColorPalette.SetColor(
            this.activeProg,
            colorFromColorIndex[colorIndex]
          );
          System.Diagnostics.Debug.WriteLine("set color " + colorIndex);
        } else {
          this.config.domeColorPalette.SetGradientColor(
            this.activeProg,
            colorFromColorIndex[this.curFirstColorIndex],
            colorFromColorIndex[colorIndex]
          );
          System.Diagnostics.Debug.WriteLine("set gradient color " + this.curFirstColorIndex + ", " + colorIndex);
        }
      });

      this.AddBinding(MidiCommandType.Knob, 1, val => this.config.domeMaxBrightness = val);
    }

    private void AddBinding(
      MidiCommandType commandType,
      Binding.bindingCallback callback
    ) {
      this.AddBinding(commandType, -1, callback);
    }

    private void AddBinding(
      MidiCommandType commandType,
      int index,
      Action<double> callback
    ) {
      this.AddBinding(commandType, index, (i, val) => callback(val));
    }

    private void AddBinding(
      MidiCommandType commandType,
      int index,
      Binding.bindingCallback callback
    ) {
      Binding binding = new Binding();
      binding.key = new BindingKey(commandType, index);
      binding.callback = callback;
      if (!this.bindings.ContainsKey(binding.key)) {
        this.bindings.Add(binding.key, new List<Binding>());
      }
      this.bindings[binding.key].Add(binding);
    }

    private bool active;
    private Thread inputThread;
    public bool Active {
      get {
        lock (this.buffer) {
          return this.active;
        }
      }
      set {
        lock (this.buffer) {
          if (this.active == value) {
            return;
          }
          if (value) {
            if (this.config.midiInputInSeparateThread) {
              this.inputThread = new Thread(MidiProcessingThread);
              this.inputThread.Start();
            } else {
              this.InitializeMidi();
            }
          } else {
            if (this.inputThread != null) {
              this.inputThread.Abort();
              this.inputThread.Join();
              this.inputThread = null;
            } else {
              this.TerminateMidi();
            }
          }
          this.active = value;
        }
      }
    }

    public bool AlwaysActive {
      get {
        return true;
      }
    }

    public bool Enabled {
      get {
        return this.config.midiInputEnabled;
      }
    }

    private void InitializeMidi() {
      this.device = new InputDevice(this.config.midiDeviceIndex);
      this.device.ChannelMessageReceived += ChannelMessageReceived;
      this.device.StartRecording();
    }

    private void ChannelMessageReceived(
      object sender,
      ChannelMessageEventArgs e
    ) {
      //System.Diagnostics.Debug.WriteLine(
      //  "MIDI channel message on channel " + e.Message.MidiChannel +
      //  " with command " + e.Message.Command +
      //  ", data1 " + e.Message.Data1 +
      //  ", data2 " + e.Message.Data2
      //);
      MidiCommand command;
      if (e.Message.Command == ChannelCommand.Controller) {
        double value = (double)e.Message.Data2 / 127;
        command = new MidiCommand() {
          type = MidiCommandType.Knob,
          index = e.Message.Data1,
          value = value,
        };
      } else if (
        e.Message.Command == ChannelCommand.NoteOn ||
        e.Message.Command == ChannelCommand.NoteOff
      ) {
        double value = (double)e.Message.Data2 / 127;
        command = new MidiCommand() {
          type = MidiCommandType.Note,
          index = e.Message.Data1,
          value = value,
        };
      } else if (e.Message.Command == ChannelCommand.ProgramChange) {
        command = new MidiCommand() {
          type = MidiCommandType.Program,
          index = e.Message.Data1,
        };
      } else {
        return;
      }
      this.buffer.Enqueue(command);

      List<Binding> triggered = new List<Binding>();
      BindingKey genericKey = new BindingKey(command.type, -1);
      if (this.bindings.ContainsKey(genericKey)) {
        triggered.AddRange(this.bindings[genericKey]);
      }
      BindingKey key = new BindingKey(command.type, command.index);
      if (this.bindings.ContainsKey(key)) {
        triggered.AddRange(this.bindings[key]);
      }
      foreach (Binding binding in triggered) {
        binding.callback(command.index, command.value);
      }
    }

    private void TerminateMidi() {
      this.device.StopRecording();
      this.device.Dispose();
      this.device = null;
    }

    private void Update() {
      lock (this.buffer) {
      }
    }

    private void MidiProcessingThread() {
      this.InitializeMidi();
      try {
        while (true) {
          this.Update();
        }
      } catch (ThreadAbortException) {
        this.TerminateMidi();
      }
    }

    public void OperatorUpdate() {
      if (!this.config.midiInputInSeparateThread) {
        this.Update();
      }

      int numMessages = this.buffer.Count;
      if (numMessages == 0) {
        this.commandsSinceLastTick = new MidiCommand[0];
        return;
      }

      var commands = new MidiCommand[numMessages];
      for (int i = 0; i < numMessages; i++) {
        bool result = this.buffer.TryDequeue(out commands[i]);
        if (!result) {
          throw new System.Exception("Someone else is dequeueing!");
        }
      }

      this.commandsSinceLastTick = commands;
    }

    public static int DeviceCount {
      get {
        return InputDevice.DeviceCount;
      }
    }

    public static string GetDeviceName(int deviceIndex) {
      return InputDevice.GetDeviceCapabilities(deviceIndex).name;
    }

    public double GetKnobValue(int knob) {
      if (!this.knobValues.ContainsKey(knob)) {
        return -1.0;
      }
      return this.knobValues[knob];
    }

    public double GetNoteVelocity(int note) {
      if (!this.noteVelocities.ContainsKey(note)) {
        return 0.0;
      }
      return this.noteVelocities[note];
    }

    public MidiCommand[] GetCommandsSinceLastTick() {
      return this.commandsSinceLastTick;
    }

  }

}