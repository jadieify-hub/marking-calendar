// These tests start WPF dispatcher threads and windows. Run in parallel on a cold two-core
// CI runner, their startup alone exceeded the handshake timeouts (releases 0.1.15, 0.1.17, 0.1.25).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
