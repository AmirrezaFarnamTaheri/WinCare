#!/usr/bin/env python3
import unittest

from tools.validate_convergence import DONOR_RE


class DonorIdentifierTests(unittest.TestCase):
    def test_accepts_two_and_three_digit_donor_ids(self) -> None:
        for donor in ("D01", "D99", "D100", "D105", "D1000"):
            with self.subTest(donor=donor):
                self.assertIsNotNone(DONOR_RE.fullmatch(donor))

    def test_rejects_noncanonical_donor_ids(self) -> None:
        for donor in ("D0", "D00", "D1", "D001", "D-01", "d01", "D10x"):
            with self.subTest(donor=donor):
                self.assertIsNone(DONOR_RE.fullmatch(donor))


if __name__ == "__main__":
    unittest.main()
