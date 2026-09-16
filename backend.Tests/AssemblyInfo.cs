using Xunit;

// Every test class talks to the same database and some flip tenant-level settings
// (approval on/off). Running classes in parallel would make those toggles race.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
