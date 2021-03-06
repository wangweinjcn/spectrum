﻿namespace Spectrum.Base {

  /**
   * An Input represents a device that receives input for Spectrum. Each
   * Visualization has one or more Inputs that it receives data from.
   *
   * A Visualizer will have a reference to the specific Input object it needs,
   * so methods regarding Visualizer's access patterns to Input are not included
   * here. Instead, only the methods that are needed for basic maintenance of
   * the Input device are included here.
   */
  public interface Input {

    /**
     * If a given Input is active, then there is a thread running that is
     * updating the Input object in some way.
     */
    bool Active { get; set; }

    /**
     * An AlwaysActive Input is active whenever the operator is enabled.
     * Otherwise, an Input needs an active Visualizer using it to be active.
     */
    bool AlwaysActive { get; }

    /**
     * This property reflects whether the Output is enabled by the UI.
     */
    bool Enabled { get; }

    /**
     * This method gets called at the start of the Operator loop (from within
     * the Operator thread) when there exists an active Visualizer that needs
     * this Input. It may be a no-op (if, for instance, the Input is being
     * updated in a separate thread).
     */
    void OperatorUpdate();

  }

}
