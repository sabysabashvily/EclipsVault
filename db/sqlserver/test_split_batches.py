"""A GO after each operation, and nowhere it would split a variable's batch."""

import unittest

from split_batches import split_batches


class SplitBatchesTests(unittest.TestCase):
    def test_an_operation_boundary_gets_its_own_batch(self):
        sql = "BEGIN TRANSACTION;\nIF NOT EXISTS (SELECT 1)\nBEGIN\n    ALTER TABLE [T] ADD [C] int NULL;\nEND;\n\nIF NOT EXISTS (SELECT 1)\nBEGIN\n    UPDATE [T] SET [C] = 1;\nEND;\n\nCOMMIT;\nGO\n"
        split = split_batches(sql)
        self.assertIn("END;\nGO\n\nIF NOT EXISTS (SELECT 1)\nBEGIN\n    UPDATE [T] SET [C] = 1;", split)
        self.assertIn("END;\nGO\n\nCOMMIT;\nGO\n", split)

    def test_a_batch_that_already_ends_is_left_alone(self):
        sql = "BEGIN\n    CREATE TABLE [__EFMigrationsHistory] ([MigrationId] nvarchar(150) NOT NULL);\nEND;\nGO\n"
        self.assertEqual(split_batches(sql), sql)

    def test_a_nested_block_stays_with_its_variable(self):
        sql = "IF NOT EXISTS (SELECT 1)\nBEGIN\n    DECLARE @c nvarchar(max) = N'x';\n    IF @c IS NOT NULL\n    BEGIN\n        THROW 50000, @c, 1;\n    END\nEND;\n\nCOMMIT;\nGO\n"
        split = split_batches(sql)
        self.assertIn("    END\nEND;\nGO\n", split)
        self.assertEqual(split.count("\nGO\n"), 2)

    def test_running_twice_changes_nothing(self):
        sql = "END;\n\nCOMMIT;\nGO\n"
        expected = "END;\nGO\n\nCOMMIT;\nGO\n"
        once = split_batches(sql)
        self.assertEqual(once, expected)
        self.assertEqual(split_batches(once), expected)


if __name__ == "__main__":
    unittest.main()
