"""Resolve the approved concept plan into validated PixelLab requests; never submit jobs."""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
from pathlib import Path

import jsonschema
from PIL import Image

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--uploads', required=True, type=Path,
                        help='JSON mapping repository reference paths to fresh PixelLab upload:<id> values.')
    parser.add_argument('--catalog', required=True, type=Path,
                        help='Current PixelLab list_tools result saved by the connected MCP client.')
    parser.add_argument('--out', required=True, type=Path,
                        help='Scratch output directory; generated arguments contain inline source images.')
    options = parser.parse_args()
    plan = json.loads((HERE / 'pixel-plan.json').read_text())
    assert plan['concept_approval']['status'] == 'approved', 'Concept approval is required.'
    profile = json.loads((REPO / plan['palette_source'].split('#')[0]).read_text())
    palette = profile['palettes']['ashen_ford']
    uploads = json.loads(options.uploads.read_text())
    catalog = {tool['name']: tool['inputSchema']
               for tool in json.loads(options.catalog.read_text())['tools']}
    source_hashes: dict[str, str] = {}

    def source(path: str) -> Path:
        file = REPO / path
        source_hashes[path] = hashlib.sha256(file.read_bytes()).hexdigest()
        return file

    def small_image(path: str) -> str:
        file = source(path)
        assert file.stat().st_size < 100_000, 'Upload large images instead of embedding them.'
        return base64.b64encode(file.read_bytes()).decode()

    requests = []
    for item in plan['items']:
        tool = item['tool']
        arguments = dict(item['arguments'])
        files = item['input_files']
        if tool == 'edit_image':
            arguments['images_base64'] = [small_image(files['image'])]
        else:
            arguments['palette'] = palette['palette']
            arguments['palette_name'] = palette['palette_name']
            if tool == 'create_image_pro':
                arguments['style_image_base64'] = small_image(files['style'])
                references = []
                for ref in files.get('references', []):
                    source(ref['path'])
                    value = uploads[ref['path']]
                    assert value.startswith('upload:'), 'Provide a real PixelLab upload reference.'
                    references.append({'url': value, 'usage': ref['usage']})
                arguments['reference_images'] = references
            elif tool == 'create_image_pro_flash':
                with Image.open(source(files['style'])) as image:
                    assert image.width <= arguments['width'] and image.height <= arguments['height']
                arguments['style_image'] = {
                    'base64': small_image(files['style']),
                    'usage_description': 'Copy pixel scale, outline, material detail and light only; draw the requested prop.'}
                arguments['style_options'] = profile['style_options']
            else:
                raise ValueError('Unsupported planned tool: ' + tool)
        jsonschema.validate(arguments, catalog[tool])
        requests.append({'id': item['id'], 'tool': tool, 'arguments': arguments,
                         'estimated_generations': item['estimated_generations']})

    options.out.mkdir(parents=True, exist_ok=True)
    for request in requests:
        (options.out / (request['id'] + '.json')).write_text(
            json.dumps(request['arguments'], indent=2) + '\n')
    summary = {'concept_approval': plan['concept_approval'], 'paid_jobs_submitted': 0,
               'estimated_generations': sum(r['estimated_generations'] for r in requests),
               'source_sha256': source_hashes,
               'requests': [{'id': r['id'], 'tool': r['tool'],
                             'canvas': [r['arguments']['width'], r['arguments']['height']],
                             'estimated_generations': r['estimated_generations']}
                            for r in requests]}
    (options.out / 'preflight.json').write_text(json.dumps(summary, indent=2) + '\n')
    print(f'Prepared {len(requests)} schema-valid requests; estimated {summary["estimated_generations"]} generations. No jobs submitted.')


if __name__ == '__main__':
    main()
