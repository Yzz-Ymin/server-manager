import importlib.util
import json
from pathlib import Path
import tempfile
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('access',Path(__file__).parents[1]/'Source/server_access.py')
access=importlib.util.module_from_spec(spec);spec.loader.exec_module(access)
with tempfile.TemporaryDirectory() as temporary,patch.object(access,'ROOT',Path(temporary)):
    path=Path(temporary)/'demo';path.mkdir()
    file=path/'profile.json';original='{"account":"test","credentials":[],"operations":{}}\n';file.write_text(original)
    def execute(alias,operation,timeout):
        assert alias=='demo' and timeout==10
        profile=json.loads(file.read_text())
        assert profile['operations'][operation]=={'command':'find /srv -maxdepth 1 -type d','sudo':False}
        return 7
    with patch.object(access,'run',side_effect=execute):
        assert access.run_once('demo','find /srv -maxdepth 1 -type d',timeout=10)==7
    assert file.read_text()==original
    with patch.object(access,'run',side_effect=OSError('synthetic failure')):
        try:access.run_once('demo','true')
        except OSError:pass
        else:raise AssertionError('Failure ignored')
    assert file.read_text()==original
print('PASS: temporary operation, default no sudo, exit status and restoration')
