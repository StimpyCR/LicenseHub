"""Validate actual HTTP responses captured by the PostgreSQL integration tests."""
import json
import sys
from pathlib import Path

import yaml
from jsonschema import Draft202012Validator, FormatChecker

document = yaml.safe_load((Path(__file__).parent / 'openapi/licensehub-v1.yaml').read_text(encoding='utf-8-sig'))
directory = Path(sys.argv[1])
for filename, schema_name in {
    'activation.json': 'ActivationResponse',
    'snapshot.json': 'EntitlementSnapshot',
    'none.json': 'EntitlementSnapshot',
    'issue.json': 'IssueResponse',
    'issue-replay.json': 'IssueResponse',
    'customer.json': 'Customer',
    'lifecycle.json': 'License',
    'renewal.json': 'RenewalRecord',
}.items():
    instance = json.loads((directory / filename).read_text(encoding='utf-8'))
    schema = {'$ref': f'#/components/schemas/{schema_name}', 'components': document['components']}
    Draft202012Validator(schema, format_checker=FormatChecker()).validate(instance)
print('8 respuestas HTTP reales compatibles con OpenAPI v1.')
