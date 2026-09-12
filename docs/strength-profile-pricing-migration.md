# Strength-profile pricing migration

Source commit `69ac210f1aeb11d35435688b198601042d502404` replaces the former flat
FDM build-preference uplift with an estimate derived from the selected slicer
profile. The .NET 10 pricing application now models the source profile inputs:

- Quality uses 0.12 mm layers and a 40 mm/s wall speed.
- Strength uses six walls, 25% sparse infill, 2 mm top/bottom shells, and a
  70 mm/s wall speed.
- Resin retains the catalog build-preference factor because it does not use the
  FDM estimator.

The source frame-chassis PET-CF regression geometry is retained as deterministic
test data. Its Strength quote must remain within the slicer-reference ranges for
time, material, and price. Additional coverage proves that the Quality profile
reports a physically longer print and a higher price than Standard, while the
existing workflow, tier, and technical-filament tests guard downstream pricing
contracts.

No source repository, deployment configuration, database, or production
environment is changed by this migration slice.
