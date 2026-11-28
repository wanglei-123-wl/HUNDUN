import importlib.util
import json
import pathlib
import tempfile
import unittest
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("boundary", ROOT / "Tools/HotUpdate/Verify-AssemblyBoundary.py")
boundary = importlib.util.module_from_spec(spec)
spec.loader.exec_module(boundary)


class AssemblyBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="AssemblyBoundary-", dir=ROOT / "Temp")
        self.project = pathlib.Path(self.temporary.name)
        for path, data in [
            ("Assets/QiDao.GameLogic.asmdef", {"name": "QiDao.GameLogic", "references": ["QiDao.Contracts"]}),
            ("Assets/Bootstrap/Runtime/QiDao.Bootstrap.asmdef", {"name": "QiDao.Bootstrap", "references": ["QiDao.Contracts"]}),
            ("Assets/Bootstrap/Contracts/QiDao.Contracts.asmdef", {"name": "QiDao.Contracts", "references": []}),
            ("Assets/Editor/QiDao.Editor.asmdef", {"name": "QiDao.Editor", "references": ["QiDao.GameLogic"], "includePlatforms": ["Editor"]}),
        ]:
            self.write(path, json.dumps(data))

    def tearDown(self):
        self.temporary.cleanup()

    def write(self, path, text):
        file = self.project / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(text, encoding="utf-8")
        pathlib.Path(str(file) + ".meta").write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex + "\n")

    def test_new_scripts_outside_scripts_are_hot_by_default(self):
        self.write("Assets/地图/NewZone/NewMonster.cs", "public class NewMonster {}")
        report = boundary.inspect(self.project)
        self.assertEqual("succeeded", report["status"])
        self.assertEqual("QiDao.GameLogic", report["scripts"][0]["assembly"])

    def test_editor_folder_cannot_leak_into_runtime(self):
        self.write("Assets/NewFeature/Editor/Inspector.cs", "class Inspector {}")
        self.assertEqual("failed", boundary.inspect(self.project)["status"])
        self.write("Assets/NewFeature/Editor/QiDao.Editor.asmref", '{"reference":"QiDao.Editor"}')
        self.assertEqual("succeeded", boundary.inspect(self.project)["status"])

    def test_fixed_layer_cannot_reference_business(self):
        self.write("Assets/Bootstrap/Runtime/QiDao.Bootstrap.asmdef", '{"name":"QiDao.Bootstrap","references":["QiDao.GameLogic"]}')
        self.assertTrue(any("depends on business" in f for f in boundary.inspect(self.project)["failures"]))

    def test_unknown_fixed_assembly_is_rejected(self):
        self.write("Assets/NewFeature/AccidentalFixed.asmdef", '{"name":"AccidentalFixed","references":[]}')
        self.write("Assets/NewFeature/Logic.cs", "class Logic {}")
        self.assertEqual("failed", boundary.inspect(self.project)["status"])

    def test_missing_script_meta_is_rejected(self):
        path = self.project / "Assets/NewCode.cs"
        path.write_text("class NewCode {}")
        self.assertEqual("failed", boundary.inspect(self.project)["status"])

    def test_current_project_has_no_unowned_scripts(self):
        report = boundary.inspect(ROOT)
        self.assertEqual([], report["failures"])


if __name__ == "__main__":
    unittest.main()
