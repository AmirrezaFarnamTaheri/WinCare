import unittest
import time
import os

class TestPerformanceBenchmark(unittest.TestCase):
    def test_file_read_performance(self):
        start = time.perf_counter()
        config_path = os.path.join(os.path.dirname(__file__), "..", "config", "maintainability-baseline.json")
        with open(config_path, "r", encoding="utf-8") as f:
            _ = f.read()
        elapsed_ms = (time.perf_counter() - start) * 1000.0
        self.assertLess(elapsed_ms, 50.0, "Configuration file load time exceeded 50ms")

if __name__ == "__main__":
    unittest.main()
