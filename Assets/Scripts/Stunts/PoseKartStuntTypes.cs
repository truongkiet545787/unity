namespace PoseKart.Stunts
{
    public enum StuntType
    {
        None,
        NormalJump,
        FlatSpin,
        BarrelRoll,
        FrontFlip,
        BackFlip
    }

    public enum LandingQuality
    {
        None,
        CleanLanding,
        CrashLanding
    }

    public enum StuntState
    {
        Grounded,
        RampApproach,
        Takeoff,
        Airborne,
        LandingAligning,
        Recovery
    }

    public enum RampType
    {
        NormalRamp,
        BarrelRollRamp,
        SpecialFlipRamp
    }
}
