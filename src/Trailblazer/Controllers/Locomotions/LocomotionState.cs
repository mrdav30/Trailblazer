namespace Trailblazer.Controllers.Locomotions
{
    [System.Serializable]
    public class LocomotionState
    {
        /// <summary>
        /// Does this script currently respond to input?
        /// </summary>
        public bool IsInControl = true;
   
        #region Locomotions

        public MoveLocomotion Move = new();

        public PlatformLocomotion Platform = new();

        public JumpLocomotion Jump = new();

        public FallLocomotion Fall = new();

        public SlideLocomotion Slide = new();

        public SwimLocomotion Swim = new();

        #endregion
      
        public void SyncState(LocomotionState other)
        {
            IsInControl = other.IsInControl;

            if (Platform.IsEnabled)
                Platform.SyncState(other.Platform);

            if (Jump.IsEnabled)
                Jump.SyncState(other.Jump);

            if (Fall.IsEnabled)
                Fall.SyncState(other.Fall);

            if (Swim.IsEnabled)
                Swim.SyncState(other.Swim);

            if (Slide.IsEnabled)
                Slide.SyncState(other.Slide);
        }

        public void ClearStateAll()
        {
            if (Platform.IsEnabled)
                Platform.ClearState();

            if (Jump.IsEnabled)
                Jump.ClearState();

            if (Fall.IsEnabled)
                Fall.ClearState();

            if (Swim.IsEnabled)
                Swim.ClearState();

            if (Slide.IsEnabled)
                Slide.ClearState();
        }
    }
}
