# Kenney assets

공식 배포본에서 Unity 프로젝트에 필요한 파일만 추가했습니다. 원본 파일과 각 팩의 License.txt는 수정하지 않았습니다.

| 팩 | 버전 | 프로젝트에 포함한 파일 | 공식 출처 |
| --- | --- | --- | --- |
| Furniture Kit | 2.0 | FBX 모델 140개 | https://kenney.nl/assets/furniture-kit |
| Splat Pack | 1.0 | 256px PNG 36개 | https://kenney.nl/assets/splat-pack |
| Impact Sounds | 1.0 | OGG 효과음 130개 | https://kenney.nl/assets/impact-sounds |

세 팩 모두 CC0입니다. 각 폴더의 License.txt를 확인하세요. 출처 표기는 필수가 아닙니다.

중복 모델 포맷, 2D 가구 렌더 이미지, 512px 중복 이미지, 벡터 원본, 홍보 바로가기는 제외했습니다. 원본 ZIP은 Downloads 폴더에 있습니다.

Unity Editor를 열면 새 파일과 .meta가 임포트됩니다. Editor 임포트와 Quest 실행은 아직 검증하지 않았습니다.

- Furniture Kit: FBX를 배치한 뒤 스케일과 머티리얼을 확인하세요. URP에서 기존 머티리얼이 올바르게 표시되지 않으면 URP 머티리얼로 변환하거나 재할당하세요.
- Splat Pack: PNG를 URP Unlit 머티리얼의 Base Map에 연결하고 Transparent로 설정해 벽 자국에 사용할 수 있습니다. PNG 자체에는 칠하기 기능이 없습니다.
- Impact Sounds: AudioClip으로 사용합니다. 타격 재질에 맞는 소리를 직접 들어보고 선택하세요.
