"""Strict current Company source and receipt consumer; no native grant or fallback."""
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import uuid
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[2]
MODULE='acceptance/MemberCompanyCatalogPersistence/current_consumer.py'
PROFILE='acceptance/MemberCompanyCatalogPersistence/current-profile.json'
BUILD='member-company-retained/build-source-evidence.json'
DEFAULTS='4ffba0639620e7e923d331317f3f28501526b573'
CONTRACTS='78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'
RESILIENCE='25f07afa51034e37b334210642d7dc5f191c142d'
HELPER='680ff227f0e4e395c44f81f40e69c845d015b637661f6e68c8c5b8828dfba14b'
ASSEMBLIES=[('defaults','Legacy.Maliev.ServiceDefaults',DEFAULTS),('resilience','Microsoft.Extensions.Http.Resilience',RESILIENCE),('http','Microsoft.Extensions.Http','4099f83977f4d29604a9aeb971fb0a7bc1d5f699'),('polly','Polly.Core','077cf388fc5a85a7f6cd0fdcb2b68d17a898019e')]
PACKAGES=[('Microsoft.Extensions.Http.Resilience','10.11.0',RESILIENCE),('Microsoft.Extensions.Http','10.0.13',ASSEMBLIES[2][2]),('Polly.Core','8.4.2',ASSEMBLIES[3][2])]
OBSERVATION_NAMES=['Legacy.Maliev.Web.Tests.CurrentCompanySourceObservationTests.'+name for name in ['LinkedInstancesAreObservedWithoutMutation','ReversedInstancesRefuseProof','SameInstanceRefusesProof','MissingInstanceRefusesProof','CycleRefusesProofBeforeUnboundedTraversal','TraversalLimitRefusesAnUnboundedChain']]
NONPASSING=('failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','completed','inProgress','pending')
# BEGIN GENERATED CLOSED SOURCE
EXPECTED_FILES={'.github/workflows/_build-and-test.yml': {'bytes': 56086,
                                           'gitBlob': 'c1558b03ab6b7eee3f385455c8d13b4974a70c7a',
                                           'sha256': '1b0a81a7bfa733114b15d8458761dce75e65db7358961c2b498142915607b7c5'},
 '.github/workflows/billing-persistence.yml': {'bytes': 31419,
                                               'gitBlob': 'c1a6047e8882d530b21749e8a854200606142743',
                                               'sha256': 'ab4175d702e4646173ecf6660b6314ca8e9ca57c46cb2f734db484023fae5156'},
 '.github/workflows/fx-joined.yml': {'bytes': 4941,
                                     'gitBlob': 'ba3bd6684215992a028bd1be7dd798b45e3746b1',
                                     'sha256': '998aeefe51671a6331db14baf5c6e47720be2fdb8e71eba9027ca7cbcc7279ed'},
 '.github/workflows/member-company-catalog-persistence.yml': {'bytes': 14572,
                                                              'gitBlob': '62d1b15343353d0ad44f0055e84d257a6e46412e',
                                                              'sha256': 'd579091b660013d7e8a36216076b7cfc4449dc8517bf94bbf84320595d27d60e'},
 '.github/workflows/shipping-persistence.yml': {'bytes': 17251,
                                                'gitBlob': '1c4fd0561c5a4ceb75e078c3bb530f7b351599c8',
                                                'sha256': '9b77b3b82a1b372432efd3f01bf580e30821db6e020389c5b3971dff3533c6f5'},
 'AGENTS.md': {'bytes': 1581,
               'gitBlob': '0af1bcba44a03e1563940bc03a81e297c609d8e2',
               'sha256': 'd7e8009d0fea39df60e34bb4e856788c3d1529c08b11959961aa3b83e3875043'},
 'Legacy.Maliev.Web.Infrastructure/Legacy.Maliev.Web.Infrastructure.csproj': {'bytes': 992,
                                                                              'gitBlob': '0b0edeb90f887078020f78420a92ea4017a93d67',
                                                                              'sha256': '2406572dfe2f68621c58947e209105112d438f46cfd8f74745502328c0d09fca'},
 'Legacy.Maliev.Web.Tests/AdditiveFxHandlerContractTests.cs': {'bytes': 27027,
                                                               'gitBlob': '065cab9a21e263ba91bb8634c0593ec6921f7cec',
                                                               'sha256': '24ea4b010bf84f27e5f1bb603359c40bd90f85d8a1c1deeeffdf43552c5c73bf'},
 'Legacy.Maliev.Web.Tests/ArchitectureTests.cs': {'bytes': 3679,
                                                  'gitBlob': 'e4e087d8b316d02b1a10e6adbd59fedf4894cc11',
                                                  'sha256': '5f70c8ce0f5b37229a9620df3a48c7591801dc65fcf96e545a012b3330f7eee8'},
 'Legacy.Maliev.Web.Tests/BillingProofLifetime.cs': {'bytes': 51759,
                                                     'gitBlob': 'bc8eb80ca2c971db63a6f46a5eb58adb91ed274a',
                                                     'sha256': '680ff227f0e4e395c44f81f40e69c845d015b637661f6e68c8c5b8828dfba14b'},
 'Legacy.Maliev.Web.Tests/BillingProofLifetimeTests.cs': {'bytes': 30056,
                                                          'gitBlob': 'da3b7fdb2f6e8892e6ac9019b188fa5dcba7d34a',
                                                          'sha256': 'b72bdc742e33eeadcac82e7f586e35ee881b4a68640beb18ab95fd04501f6541'},
 'Legacy.Maliev.Web.Tests/InstantQuotationSubmissionStoreTests.cs': {'bytes': 34744,
                                                                     'gitBlob': 'fbef447eeeec1b15bba75d2696ae589343bcfc42',
                                                                     'sha256': '00d91ab71e14620e7198853cfba144d821c83f3722c9fb5c745f91fce5d8f04d'},
 'Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj': {'bytes': 1901,
                                                            'gitBlob': '28cd8eb5af11657ff6cfe4c9ba6d8b384ef608b1',
                                                            'sha256': 'e0929c2f45309057efb29c2448b9c47b1971ef39dfafa54e407a248a83596576'},
 'Legacy.Maliev.Web.Tests/LegacyServiceDefaultsIdentityContractTests.cs': {'bytes': 6887,
                                                                           'gitBlob': 'e4ac1003ebfa4b546281366828613db17b9e49d7',
                                                                           'sha256': '3d41cc5d47a3e2aadf4082c6159d31d04a147baf92dbfa8fc088fd26426beeb7'},
 'Legacy.Maliev.Web.Tests/RuntimeAssemblyProbe.cs': {'bytes': 5247,
                                                     'gitBlob': '349d1544b8e4659bc84176e3008de37ac0134261',
                                                     'sha256': '0b040cf4e7ece8d6130403137dcd81d9d0e3bf9f6f234faa41af58651c5b17a1'},
 'Legacy.Maliev.Web.Tests/ThaiLookupBillingPersistenceTests.cs': {'bytes': 25876,
                                                                  'gitBlob': '20cf6977fcafa36bc4e2026d727e093abf81ed29',
                                                                  'sha256': '0ab995210bbce6cd53219edc2abafd63c3bfec7ffdbf2d7ae6935eace4068761'},
 'Legacy.Maliev.Web.Tests/ThaiLookupShippingPersistenceTests.cs': {'bytes': 24560,
                                                                   'gitBlob': '58414d1a92d49423272c67a84b2c820c553a6c1d',
                                                                   'sha256': 'c951fa99b9ec2ec17d3de0505080c5b46694ed85aaad6044415cd3fd5218f128'},
 'Legacy.Maliev.Web/Legacy.Maliev.Web.csproj': {'bytes': 3598,
                                                'gitBlob': '7226f0d543247bc73ec4adcbccec7211df644511',
                                                'sha256': '8d8a50a193394de6127d37f50871d4a7e10d35e4b63f104c93d2681d43033f80'},
 'acceptance/CareerOriginalMaterialization/CareerOriginalMaterialization.Tests.csproj': {'bytes': 822,
                                                                                         'gitBlob': '9105df41e793cf7c1fa7a41154f3a11f8eb002e3',
                                                                                         'sha256': '604c73f7be0f01e4281855df50427e5a2a70009f5ba702063a93f30372c76d0f'},
 'acceptance/MemberCompanyCatalogPersistence/README.md': {'bytes': 10624,
                                                          'gitBlob': 'bcba6d368601dbbd826a6a7cc7d635964beacb45',
                                                          'sha256': '54d615ee241988ba2e0a9735e80354e7618c3f209426cf11081bd8d7832e1a9f'},
 'acceptance/MemberCompanyCatalogPersistence/Tests/CompanyRuntimeAssemblyProbe.cs': {'bytes': 340,
                                                                                     'gitBlob': '8218a016642174902ce8c03b3509f97324744fd5',
                                                                                     'sha256': '743cbc17e5ea6c9bd7452157755a2f11d95bcc17b5cb0688c3a68878165e152f'},
 'acceptance/MemberCompanyCatalogPersistence/Tests/CurrentCompanySourceObservation.cs': {'bytes': 8099,
                                                                                         'gitBlob': 'd800e29e741b097164f2c32da35e6c4d3b99210f',
                                                                                         'sha256': '2e739f248b06a35c96e7d0c03a1f7448ce5d3f983114fb1b5b6f12a3253f5401'},
 'acceptance/MemberCompanyCatalogPersistence/Tests/CurrentCompanySourceObservationTests.cs': {'bytes': 3531,
                                                                                              'gitBlob': '412c0c1456107cf7f67432fe8f08832119cd0645',
                                                                                              'sha256': '4e6afcc904b7548ba96abcfd5b1d5fb08f7f2310ddea628f2a0e16e84f48f746'},
 'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistence.Tests.csproj': {'bytes': 699,
                                                                                                   'gitBlob': '153e1191e101dc146db873fbd42ed29cddedb5d2',
                                                                                                   'sha256': '990ee1f053bbb02bbdbb08d49989aa88b1cf80fb08604f0e2e09cffe2d8f7ed2'},
 'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistenceTests.cs': {'bytes': 33043,
                                                                                              'gitBlob': 'ed4f04607af425f7aef016bc769eca4449436c99',
                                                                                              'sha256': 'e3105d409f45cf58718c3e31f1a208495086f31fa7eaed067320c9f5f33c1b91'},
 'acceptance/MemberCompanyCatalogPersistence/capture_build_source.py': {'bytes': 18247,
                                                                        'gitBlob': 'e0ec4b32e20fb273e5c8c4151c25577df80c062d',
                                                                        'sha256': '0e5a74433df9bcbaf431ed0763d2377f21c2eb0f0c5b5eee801ed2b7f94785e2'},
 'acceptance/MemberCompanyCatalogPersistence/historical_controls.py': {'bytes': 3943,
                                                                       'gitBlob': 'c0d537b2ce03a9bf9830f959cd193f14f2cb24f0',
                                                                       'sha256': '30d6d022f73537483ef2eddd07796732d89f42b92c3108f3e2c9fd9715e91ada'},
 'acceptance/MemberCompanyCatalogPersistence/lifecycle-rosters.json': {'bytes': 15844,
                                                                       'gitBlob': '97cc0016e40bd759ee929ae99bdc4e263bfa35e7',
                                                                       'sha256': '8e9561190c803eebdb89bddbe529e1f75b306ca3d6916620053a79d7bdd88988'},
 'acceptance/MemberCompanyCatalogPersistence/nuget_signed_verification.py': {'bytes': 5731,
                                                                             'gitBlob': '8a0a98cb3409f39e83725b62904a7bdfcc422b14',
                                                                             'sha256': '8d183f689073f3a2454b77144a5e6c2fa982d4fb6020e683afa9bed9f1b32410'},
 'acceptance/MemberCompanyCatalogPersistence/predecessors/before-current-0.bin': {'bytes': 18505,
                                                                                  'gitBlob': 'f3a653f8c6653a476433d8a7cb4956d55ee58b21',
                                                                                  'sha256': 'eb3390ae61f09116253210c7e337c2d366ebc97011d8d83dd260a98b10157516'},
 'acceptance/MemberCompanyCatalogPersistence/predecessors/before-current-1.bin': {'bytes': 698,
                                                                                  'gitBlob': '4a16afea49d08f32d019a23e0766580024d40343',
                                                                                  'sha256': '365b519a6b46d806a91f62d9c92314d5c053cec30b67c1a46289da1140d9b0a7'},
 'acceptance/MemberCompanyCatalogPersistence/predecessors/before-current-2.bin': {'bytes': 32564,
                                                                                  'gitBlob': 'e4b813901dbe30f48a5625dd69fa90aa0a33222b',
                                                                                  'sha256': '64ab3ce503697cdb04aa5c5d8e50504d67bb1d17b010295b833267647f521fa7'},
 'acceptance/MemberCompanyCatalogPersistence/predecessors/before-current-3.bin': {'bytes': 19796,
                                                                                  'gitBlob': '2e4c024e1562706ad43374cbdec5222c62b7c8f9',
                                                                                  'sha256': 'f06dbcebf9cb462764db40c5462c70e6fafb3be2b794017b9d43a1c4c7040d6c'},
 'acceptance/MemberCompanyCatalogPersistence/predecessors/before-current-4.bin': {'bytes': 8679,
                                                                                  'gitBlob': '475d88327eb3caf0784c71b604796c2473a3dbc9',
                                                                                  'sha256': '80705a486116ddff2f571526aaefbbb3bc14f5e0d26339faae7e4620f1306638'},
 'acceptance/MemberCompanyCatalogPersistence/predecessors/helper-pr527.cs.bin': {'bytes': 46531,
                                                                                 'gitBlob': 'f350d0fdd6224d6ca4d8745b272de85a41c7f45a',
                                                                                 'sha256': '096be55636a60184c452ab0c85a562d0b4b0926e310e95c0a6cf841f00bfccd4'},
 'acceptance/MemberCompanyCatalogPersistence/retain.py': {'bytes': 20429,
                                                          'gitBlob': '62e3978d390f57291d35fd56484cc96f2d4a342d',
                                                          'sha256': 'db6def19fadfc7b71b9096257c3c37739e72935201775ba2ecbd336bacf9e3ac'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/assembly_binding.py': {'bytes': 1453,
                                                                                  'gitBlob': '233cdfda70dec523b14d754da95724cebe23b091',
                                                                                  'sha256': '64fc6a49db129ea35c0457044350eecde4be8fa9efbc53f6d37981078ee40551'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/birth_time.py': {'bytes': 988,
                                                                            'gitBlob': 'c69279d90c539cd6d26717a90b2917c979704314',
                                                                            'sha256': '63c89b4ee18f4c764d38acdf89d66f266b5e05379e7ed83370debbebf6f9ecb7'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/cgroup_controls.py': {'bytes': 1416,
                                                                                 'gitBlob': '5c6f810ae77dfa543a126b20cc9c3e0ab74d5c4b',
                                                                                 'sha256': 'f1497289a5a674a7f11153b53010fb9235d35c8e35ecb08eceead3a8d6c45c06'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/cgroup_lease.py': {'bytes': 5959,
                                                                              'gitBlob': '93318cb490ece4a7d918eca35b928eee6f53b490',
                                                                              'sha256': '51f8be925298b9e781c46a34bced64d8182eb97770d28067b5a41023c42b9ce0'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/claim_testhost.py': {'bytes': 1676,
                                                                                'gitBlob': '073c1b19a4cb7102daf818032a8a41eb25fee0d9',
                                                                                'sha256': '0e906f61204b6d498e8f09b78af344e5a328530338d0b9e3009d595c1f1c3548'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/hosted_probe.py': {'bytes': 13617,
                                                                              'gitBlob': 'a7e34706fec070f67b8d6fe44d36063d408f8a70',
                                                                              'sha256': '56a40aa9fa49fe596f6fd75d139c30c775ac46e5370d34f96392dd72b732415f'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/identity_control.py': {'bytes': 3649,
                                                                                  'gitBlob': '56a70b83aa5016209f1574ead6b0a7b940a41303',
                                                                                  'sha256': '043a6dc0c0056991ede37b6572227fdddc461ca9aa6e46ed9721e87e8ab493b2'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/memory_admission.py': {'bytes': 515,
                                                                                  'gitBlob': '6b2fa2002985f921361cb610e4b7b5fec11b7e57',
                                                                                  'sha256': '96b266f5d9270053e0661fef83874fc188ed02a1c91dc1d306d913b2f94204a8'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/owned_receipt_file.py': {'bytes': 1497,
                                                                                    'gitBlob': '4f275540459ff764940fdadfcad82080f922d835',
                                                                                    'sha256': '2d1b403ef37fd7974630984191803051aa71c5fb7ae659c7151e4f59402ad3b2'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/process_ownership.py': {'bytes': 1863,
                                                                                   'gitBlob': '60503d54895358105e4f8122f74ed27e0be4d50d',
                                                                                   'sha256': 'b15d711f66efa49d06d5e5c4bea8f9437879c74566e4e259fc33855a363d9ad2'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/process_settlement.py': {'bytes': 1131,
                                                                                    'gitBlob': 'db587a5628ab4606f1f95e4eae6a84bfbe8cc8a3',
                                                                                    'sha256': '39c7252da9a39053384c4e9ff156ae4573d7e3f4bbcd4fed49cd9daab97f6ba3'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/run_hosted.py': {'bytes': 1052,
                                                                            'gitBlob': '34f47fb148d246497d0536433987bce649021de6',
                                                                            'sha256': 'a99f4d54f558ecf98aac01f60d0749c1aa2f888ba55adf190f824a17a36e8ac9'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_all_controls.py': {'bytes': 745,
                                                                                   'gitBlob': '094674057b7f913ee18cb1a48f7f7304812978c7',
                                                                                   'sha256': 'aa0a696a7900f70637bf77bdaf359cd341271774e03caa08c64b0a7b9b011388'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_assembly_binding.py': {'bytes': 1658,
                                                                                       'gitBlob': '1868deb0e21a82d6ec2d5751cd453c8cab203c11',
                                                                                       'sha256': 'aae9568c794a0638a7b7451a9d8e45d11b257fc95836786826fda688265d632d'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_birth_time.py': {'bytes': 728,
                                                                                 'gitBlob': 'dcb15fb9499fb04940db766bb2c480b4b7ae6480',
                                                                                 'sha256': 'b20a75bb34f8b884dcb22eaf924c174fbd0a37a7cd11f17f9b9efc3297f72be7'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_cgroup_controls.py': {'bytes': 1317,
                                                                                      'gitBlob': '115d45cbcb865664a5aad3705b50ef30a3173f63',
                                                                                      'sha256': '38ecce533bd2b2645397a85dfd9a0b3a8fcbbcbcf489471aa36f6bda2c9bba8d'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_cgroup_lease.py': {'bytes': 6084,
                                                                                   'gitBlob': '54e3a6ef1b5aab6987338120a5513479e95bc3e0',
                                                                                   'sha256': 'c3fbb299d42a1201075b609b040b1d98df0688e8cb64e262fc78f065137f1203'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_claim_testhost.py': {'bytes': 3069,
                                                                                     'gitBlob': '0fd33099b3890027a069188a2d395288c65ad2c2',
                                                                                     'sha256': '52ef391aa22b78b9472fe04c706a26081acfce09b06d22620b6d38ceaf7de82d'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_hosted_probe.py': {'bytes': 13629,
                                                                                   'gitBlob': '6f4fb75a8a04f4cd628c7cd308300bf8fa0620e1',
                                                                                   'sha256': 'b26fa05d335a5ba76bc53959437c2335d16a2abbdc2d8344f2c775e39a3a1470'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_identity_control.py': {'bytes': 2024,
                                                                                       'gitBlob': 'b62fe5b17dfb0571750e73e463e05c059124e30b',
                                                                                       'sha256': '15fb5383a6d511aca1da5eeec4b145aaf4a02f5e59e4f1d58eef61c9f4b8b09c'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_memory_admission.py': {'bytes': 611,
                                                                                       'gitBlob': 'fdd9191eea043d0acd8e397a093e4ef11400694a',
                                                                                       'sha256': '87ee5254b7c4042dc1e303a1525bcfb0797c783f6be1edfcfcdceed49df50549'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_owned_receipt_file.py': {'bytes': 2688,
                                                                                         'gitBlob': 'eac5da161ff3e333f1031be0b76286a33bad9c52',
                                                                                         'sha256': 'd930cb4ba3a8cc888419da8d68c4513a452e9c405e2048446865e5508697045d'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_process_ownership.py': {'bytes': 2050,
                                                                                        'gitBlob': 'fd9a221df424f9adb74537718c97f3a13bccfa53',
                                                                                        'sha256': '07a8c31762980cd83b47d96a5cf300b8c3ec020068d60c1534ef536efc2ec6ba'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_process_settlement.py': {'bytes': 1737,
                                                                                         'gitBlob': 'd825767b9c554d786977d641be32f41b376921a5',
                                                                                         'sha256': 'bb9d614cd3ae714c61075a36e62305932cc51ffb8795784af717a15530520d7f'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_run_hosted.py': {'bytes': 1560,
                                                                                 'gitBlob': '22b3a6af79d08c3688faa1904a7af6e8c636b31f',
                                                                                 'sha256': '6df66bebc7d990a4ab3de5b2bd9559bd1ef0980b092f9eb23842fb70878a3aab'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_trx_control.py': {'bytes': 1826,
                                                                                  'gitBlob': '1c72adcd99925eb856fd4a919879081f5e16ba35',
                                                                                  'sha256': 'bf8df57c48112e18a1f070daa839d06fa58f7faeaec0660dae31a6a89bb21d1f'},
 'acceptance/MemberCompanyCatalogPersistence/runtime_probe/trx_control.py': {'bytes': 2491,
                                                                             'gitBlob': 'b44a38cb0bffa50b2b6d0c9b05c553b5316496aa',
                                                                             'sha256': '16a0d8f10fde54a545ee30e4a306942a5e6325f8659190e66f4e9d4d432f3e16'},
 'acceptance/MemberCompanyCatalogPersistence/test_current_consumer.py': {'bytes': 30972,
                                                                         'gitBlob': 'c3a484d39073a090a6474a3e6bd1f4a4d1a1711c',
                                                                         'sha256': '896712f053ede28458b7b3cf963026a00df31e1cb802db0682192baf25615c4c'},
 'acceptance/MemberCompanyCatalogPersistence/test_retain.py': {'bytes': 22334,
                                                               'gitBlob': '008b5ed1a5d3d3ef2675fe98989e64eeaff1e78f',
                                                               'sha256': '8827cd060a130a8f8c4b5fac24493f4f62d7c0b5aa99b6430155cdb8df0a4e3a'},
 'docs/member-billing-catalog-persistence-proof.md': {'bytes': 10433,
                                                      'gitBlob': '4a80386ae153e479f86469a7ba3d5b6df7310e9b',
                                                      'sha256': '02c1a46fd5ea756680d6900e996d642e95e7e830966bda8fd859b604fca57452'},
 'docs/member-shipping-catalog-persistence-proof.md': {'bytes': 2730,
                                                       'gitBlob': 'b770a56a24c4e954f9e5d8bcad397e4532f3a854',
                                                       'sha256': '7e79575157f2d7292011988a2a20198e7931e21306c4fb3f4b4ca669f4c014bb'},
 'docs/web-sealed-source-transport.md': {'bytes': 6387,
                                         'gitBlob': '4f096c3a432ce545faa7a666b8febdead32823e8',
                                         'sha256': 'e972896a018eabc97bbbc613c6fd65f6eb61f0277e8df175860e6ca20908fbd8'},
 'scripts/retain-billing-initial-probe-comparison.py': {'bytes': 12958,
                                                        'gitBlob': '6fe5d26b35848e84adc3141ff7b196d9891d590d',
                                                        'sha256': '7fedac1baeb3e9e9127ca84d9577e4bbd20845b5ff35c1624a8994e33d32874f'},
 'scripts/run-fx-joined-hosted.py': {'bytes': 19307,
                                     'gitBlob': '2b677e24574d08ece4003b25fb48aac1befed50b',
                                     'sha256': '7b012617049263ccc94cfb86b70be06ddd128c7aabac31cb6b0ba6019f17aba8'},
 'scripts/tests/test_billing_receipt_attempt_custody.py': {'bytes': 21616,
                                                           'gitBlob': 'f2599691099861f52ad3ccfd7430d4bf48870e05',
                                                           'sha256': 'dc20868d5bae5e62d590a151b227188005b55ce461b50de8d7225eb1a21cb257'},
 'scripts/verify-billing-persistence-proof.py': {'bytes': 7288,
                                                 'gitBlob': '41972b01df4c1f65c251df0a76f0187e53781fac',
                                                 'sha256': '5ae4081f8c4b09e32627854b2345529b8457cc737f1d5496cc7d2ab293f57e11'},
 'scripts/verify-focused-web-trx.py': {'bytes': 26212,
                                       'gitBlob': '9f249235eb32e89f45f543659d6011844c789e1b',
                                       'sha256': '113fb4f759692362972921ac610a12b6f812beeaf1a6f909a9766e36317db731'},
 'scripts/verify-fx-browser-assets.py': {'bytes': 2375,
                                         'gitBlob': '3bc8f7c5696b51a5a6c726c3a3201db8a5b02767',
                                         'sha256': '27bcd97e957d50733c1b482b0aa5b0161153c0f901ec0d249debcf8ca1b372c1'},
 'scripts/verify-shipping-persistence-proof.py': {'bytes': 13737,
                                                  'gitBlob': 'cec940bfe054cbee150715a7e19a5a357e56bec2',
                                                  'sha256': '4a9c9b8a1a74a3fbe742f4775686569adc47de220f98f96797d18a519079d534'},
 'tests/fx-joined/FxJoined.Tests.csproj': {'bytes': 1138,
                                           'gitBlob': '1b6bbffc2d65ee2f8ed2f66c965ff1f9e630f4ec',
                                           'sha256': '48557ccec2591f713ae58473267c27943ef9d352379826f2243dd0579ca0c90d'},
 'tests/fx-joined/JoinedFxNativeHttpTests.cs': {'bytes': 29127,
                                                'gitBlob': 'edbd7c6676863b2c80163dddd752d6655d655a49',
                                                'sha256': 'a468e69bbe2d172e121be5c7901b691619857e6a7779f92c98303604e6f18fd6'},
 'tests/fx-joined/README.md': {'bytes': 2302,
                               'gitBlob': '0245a5fece93f21e223da96354f08ea3a261c9e8',
                               'sha256': 'bc2e4c40dd2da1f7cd2710e40cf0858584774630257a584a48896d74716b0eaf'},
 'tests/fx-joined/candidate-graph.json': {'bytes': 1315,
                                          'gitBlob': '489ce73b1cdf40ff9d246042d95f60d4024eaa82',
                                          'sha256': '21179877c295d504d281442ca8b7bff9f70a00b4468ab0b608f3a57b748fb190'},
 'tests/fx-joined/test_orchestration_guards.py': {'bytes': 4863,
                                                  'gitBlob': 'b10cf37d684d09919b1bcc1f5f7eacc62d2a0a4e',
                                                  'sha256': '9891f43d7b7a2c1e00065cc47b3dd04b9c084ad1bc1a1f7d072b4d25eef072ee'}}
BASE_GIT_ROWS={'.dockerignore': '100644 blob d372d33370c82da901943ced138e4d8f0e4cc811',
 '.editorconfig': '100644 blob 582bd410b7b1a86cd9cc1d0a85e18361d3e985bd',
 '.gitattributes': '100644 blob 1966dea355f7fae9a348f61f580c3a3428d93788',
 '.github/dependabot.yml': '100644 blob 0c31a3b61d48441829134fdfa2506366ed273652',
 '.github/workflows/_build-and-test.yml': '100644 blob c1558b03ab6b7eee3f385455c8d13b4974a70c7a',
 '.github/workflows/billing-persistence.yml': '100644 blob c1a6047e8882d530b21749e8a854200606142743',
 '.github/workflows/ci-develop.yml': '100644 blob c1bf874ecf39eab2864889fb4f2d8b38d1017bb0',
 '.github/workflows/ci-main.yml': '100644 blob 56aa263726fd6b3f767e4044c9037b0c7b54d7d5',
 '.github/workflows/ci-staging.yml': '100644 blob 789a908f0061215271607ac8b53ed859f7370c88',
 '.github/workflows/corporate-logo-webp-proof.yml': '100644 blob e5e5a720b4ea03dad1396300bc913a8b7be1f976',
 '.github/workflows/fx-joined.yml': '100644 blob ba3bd6684215992a028bd1be7dd798b45e3746b1',
 '.github/workflows/member-company-catalog-persistence.yml': '100644 blob f3a653f8c6653a476433d8a7cb4956d55ee58b21',
 '.github/workflows/pr-validation.yml': '100644 blob 30d6bbc8a8dac536e60a9c4d23c39cd204019a94',
 '.github/workflows/publish-image.yml': '100644 blob 152837ffaf865df019a2168063af7165670ec93e',
 '.github/workflows/release-asset-coverage.yml': '100644 blob 67d524d406b432c4a187afdab67aea7fbf6a2e8c',
 '.github/workflows/shipping-persistence.yml': '100644 blob 1c4fd0561c5a4ceb75e078c3bb530f7b351599c8',
 '.github/workflows/web-candidate-qualification.yml': '100644 blob 04d5ea706ee31789fa8736e68bad11ac532b7ac0',
 '.github/workflows/web-country-source-qualification.yml': '100644 blob bf5345715b52cfcced72954627538563f859de18',
 '.github/workflows/web-packaged-image.yml': '100644 blob cf284de9031d84dddc6b5a7c3090794e740c851f',
 '.gitignore': '100644 blob 65b4aa47cc52c8f803db4a085c679ebe1ca9ba4f',
 '.gitleaks.toml': '100644 blob 1498a1a6f77657891e5bb4a76a6e378b62d6a9cc',
 '.superpowers/sdd/task-4b-report.md': '100644 blob d4bb00a9d1d8d74aee8c589f439b7596e3d84f7d',
 'AGENTS.md': '100644 blob 0af1bcba44a03e1563940bc03a81e297c609d8e2',
 'Directory.Build.props': '100644 blob 5e514ea0a0e4e8db3184020c136fe05145a46c9e',
 'LICENSE': '100644 blob c5cb10e9b5a8bbadd6aad8db9627a8f3484516ea',
 'Legacy.Maliev.Web.Application/AccountContracts.cs': '100644 blob f5164d57f703629ea5401da7bcb53196dc395f58',
 'Legacy.Maliev.Web.Application/AssemblyInfo.cs': '100644 blob 2bd1e87896894f2c3f39f87fc4d83651774afe04',
 'Legacy.Maliev.Web.Application/CareerContracts.cs': '100644 blob 3883e7d00500be5021d049134f68145418140c94',
 'Legacy.Maliev.Web.Application/CncFileFinalizationContracts.cs': '100644 blob 9e3d6f56ada4458d03238be25df0faaaa77ff1bc',
 'Legacy.Maliev.Web.Application/CncFileTransportContracts.cs': '100644 blob c423bb3dc7252952c4da475173bd19eb0d2e7fe9',
 'Legacy.Maliev.Web.Application/CncProfilePersistenceContracts.cs': '100644 blob e2de7113e3cd7c9be7d9b022270152017d8f5b1a',
 'Legacy.Maliev.Web.Application/CncQuotationAvailability.cs': '100644 blob 19d012ab7946589be2d6ef6faf2bb2e2ce91417d',
 'Legacy.Maliev.Web.Application/CncRequestContracts.cs': '100644 blob 323f37dc1c3f460248ddc253ded620af0d128f74',
 'Legacy.Maliev.Web.Application/CncSignedLinkContracts.cs': '100644 blob c6072ac394b6a816258f6b80e59252f647e5ae3c',
 'Legacy.Maliev.Web.Application/ContactContracts.cs': '100644 blob 81d6e612d48931d3e9203c12a401b47063917eef',
 'Legacy.Maliev.Web.Application/CustomerDocumentContracts.cs': '100644 blob 383e6ec060db2a17b1b9195cfa1a774b5ce1f02f',
 'Legacy.Maliev.Web.Application/CustomerMemberDetailContracts.cs': '100644 blob 512d93657a7e5cb90e09bfabfd2a1ab6a39ea0e7',
 'Legacy.Maliev.Web.Application/CustomerOrderCatalogContracts.cs': '100644 blob 6f0461ef8079c8a696e38f490e348a2d52de61e9',
 'Legacy.Maliev.Web.Application/CustomerOrderContracts.cs': '100644 blob 6a085c7932c3eab1396b528b91c5e59ddd49a120',
 'Legacy.Maliev.Web.Application/CustomerOrderSubmissionContracts.cs': '100644 blob e330a670f4a1b9baca08244aed4b783ed71ceaf8',
 'Legacy.Maliev.Web.Application/CustomerOrderSubmissionService.cs': '100644 blob f27d19249b5eae9626ab7687d50c62dd51b724f8',
 'Legacy.Maliev.Web.Application/CustomerQuotationContracts.cs': '100644 blob 08192ab4651616b774bc997c4f4d1e0c0c0aed96',
 'Legacy.Maliev.Web.Application/CustomerQuotationSortQuery.cs': '100644 blob 53f6ec892061386c9db1ca0ab4534a551d4b67d0',
 'Legacy.Maliev.Web.Application/InstantQuotationAdmittedMeshService.cs': '100644 blob d9218772e66f93a0387ae87acf27e22b77793b1b',
 'Legacy.Maliev.Web.Application/InstantQuotationAuthenticatedPreparationService.cs': '100644 blob 8d73df83a666d9196198e77f39e56d16b300f00c',
 'Legacy.Maliev.Web.Application/InstantQuotationAuthenticatedProfile.cs': '100644 blob 912071e05d77f3517b645f8eb7a75f4d5aaa39ac',
 'Legacy.Maliev.Web.Application/InstantQuotationAuthoritativePricingService.cs': '100644 blob a9b81f01f6ed4acbe5b27378b9cfe76d346162f2',
 'Legacy.Maliev.Web.Application/InstantQuotationBoundPhysicalAnalysisService.cs': '100644 blob ced14bdd5d90055a50adf0fdda874e0c33ff3cd5',
 'Legacy.Maliev.Web.Application/InstantQuotationContracts.cs': '100644 blob 713fc1cc16dcae8d602afed056a274b989057566',
 'Legacy.Maliev.Web.Application/InstantQuotationExchangeRateContracts.cs': '100644 blob 9f087248eb29abe699cde29456b1d5f47d2c6d3c',
 'Legacy.Maliev.Web.Application/InstantQuotationFileCapabilityContracts.cs': '100644 blob c7c5bc02bb231e83da7b6cbf267bdfd3865e43b5',
 'Legacy.Maliev.Web.Application/InstantQuotationFulfillmentContracts.cs': '100644 blob f1012ec91142a9bd132ee919476d279537852e15',
 'Legacy.Maliev.Web.Application/InstantQuotationFulfillmentCoordinator.cs': '100644 blob cf69283a5392ad70af3d3e7871d59cc53d9bce78',
 'Legacy.Maliev.Web.Application/InstantQuotationGltfUnitPolicy.cs': '100644 blob 90fa32ea146d594da784299157c0b1cad609bf05',
 'Legacy.Maliev.Web.Application/InstantQuotationMaterialPricingProgress.cs': '100644 blob 323594ace359bd9676703b0aef858fca33641db0',
 'Legacy.Maliev.Web.Application/InstantQuotationPhysicalAnalysisInputContracts.cs': '100644 blob 327bf219fd69b2eafaccf905bc315b24f7e84534',
 'Legacy.Maliev.Web.Application/InstantQuotationPhysicalAnalysisReceipt.cs': '100644 blob ca02e52e397bc407e18c72adb07dd5823843b7c7',
 'Legacy.Maliev.Web.Application/InstantQuotationPricingService.cs': '100644 blob c93defc12e7e85587f185a1852ca2c47527d5d98',
 'Legacy.Maliev.Web.Application/InstantQuotationProfileCompletionContracts.cs': '100644 blob 510d6dcafaff58c8981fc378007248ce8cdb1dd3',
 'Legacy.Maliev.Web.Application/InstantQuotationSubmissionContracts.cs': '100644 blob 156ab0bcbb6f6d800d4ef8c0a85d56c148ccb496',
 'Legacy.Maliev.Web.Application/InstantQuotationSubmissionService.cs': '100644 blob b5d42659539274a6eded06cf6252ca700062c6d1',
 'Legacy.Maliev.Web.Application/InstantQuotationUploadContracts.cs': '100644 blob 015e6320a91e811d738170ffe063c989670d001e',
 'Legacy.Maliev.Web.Application/Legacy.Maliev.Web.Application.csproj': '100644 blob ed2975859f52ef62b12433f6efb8619aed573dbf',
 'Legacy.Maliev.Web.Application/NotificationContracts.cs': '100644 blob 9c05ac13a7cb5d3831b778880a97b52fcb6ee557',
 'Legacy.Maliev.Web.Application/Pricing/AdditiveGeometryValidator.cs': '100644 blob 9fc48462af354088dc4db6dfce8f8b0c03e6f1ea',
 'Legacy.Maliev.Web.Application/Pricing/AdditiveLeadTimeCalculator.cs': '100644 blob 9c730a65052125166424850c4f046ac102f7cfdc',
 'Legacy.Maliev.Web.Application/Pricing/AdditiveOrderCostCalculator.cs': '100644 blob 7fd3235d479e668f1888a485729f599a1f0202db',
 'Legacy.Maliev.Web.Application/Pricing/AdditiveQuoteTickets.cs': '100644 blob e394553497a6df27fb448d9d6dec03fe07abee8b',
 'Legacy.Maliev.Web.Application/Pricing/CncCommercialCalibration.cs': '100644 blob 1daf285151c46bf288e64bbc0eab544d48b759a8',
 'Legacy.Maliev.Web.Application/Pricing/FdmBuildProfile.cs': '100644 blob 9fb5ef21f1937e1527141d74e66825fee4988d09',
 'Legacy.Maliev.Web.Application/Pricing/FdmRuntimeProfileCatalog.cs': '100644 blob 9cf85b13b013c8ce7b80263777157b238efc0f5d',
 'Legacy.Maliev.Web.Application/Pricing/PricingCatalog.cs': '100644 blob af37e8abaade2d4e0dcd4d6347a5c0ffaf957b10',
 'Legacy.Maliev.Web.Application/Pricing/PricingEngine.cs': '100644 blob 2d95098b4e26a5f884d46f6e8fa8e63fb6f42464',
 'Legacy.Maliev.Web.Application/Pricing/PricingModels.cs': '100644 blob 0a50c10224eb376644ffbe37cc70c0f2c8cf0227',
 'Legacy.Maliev.Web.Application/Pricing/PrintTimeCalculator.cs': '100644 blob c811d5aa39abc08a81c78a3f9a9d7a73055aa609',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Resolved/maliev-esun-abs-esd-x1c-filament.resolved.json': '100644 blob '
                                                                                                           'a5d0c5ada6e4d0af55479273fd6425e8237adc45',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Resolved/maliev-esun-abs-esd-x1c-process.resolved.json': '100644 blob '
                                                                                                          'a4ded29a8fb1bc3ce13698b5ab4487e81c774823',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Resolved/maliev-fiberon-pa612-esd-x1c-filament.resolved.json': '100644 blob '
                                                                                                                'd0d2106e11a0a2ef38e466bae442f6c1aae40c8b',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Resolved/maliev-fiberon-pa612-esd-x1c-process.resolved.json': '100644 blob '
                                                                                                               '528742d5ea8a62239939010c81e03980c1a1856e',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Sources/esun-abs-esd-x1c-filament.json': '100644 blob a257cae378a9ad2fcd69455541722c02d7d92ae7',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Sources/esun-abs-esd-x1c-process.json': '100644 blob 6f0e22a7df49c71e293e687d79c9bc4556aeca8c',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Sources/fiberon-pa612-esd-x1c-filament.json': '100644 blob 8a74d06df1d27c32584fc19feffc557563d003d8',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Sources/fiberon-pa612-esd-x1c-process.json': '100644 blob 3e920f97bb472db4210742aac1e63ea4b8a4af16',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Sources/maliev-esun-abs-esd-x1c-filament.json': '100644 blob '
                                                                                                 '6bf25b938471d73280f5e0b3ca12487b36be9fc9',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Sources/maliev-esun-abs-esd-x1c-process.json': '100644 blob '
                                                                                                '18a3bdcf9d4bf761fd30e34d91dfddb85e659157',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Sources/maliev-fiberon-pa612-esd-x1c-filament.json': '100644 blob '
                                                                                                      'bb56a1f6685c4ca25d2b745f5fd0f6b0b878b29b',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/Sources/maliev-fiberon-pa612-esd-x1c-process.json': '100644 blob '
                                                                                                     '5c12dec8af0c2353af1ca457a7fbe365d88bf534',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/esd-profile-evidence.v1.json': '100644 blob 6278a6f4fa55ed8753a2293efae2d9d2a145f86e',
 'Legacy.Maliev.Web.Application/Pricing/Profiles/runtime-fdm-profiles.v1.json': '100644 blob 1dafd630923d097d090fcafb1260b539a367d046',
 'Legacy.Maliev.Web.Application/Pricing/ResinBuildProfileEvidence.cs': '100644 blob 9d580f86b61acd8931f1ad8c6c40830c7a897dd0',
 'Legacy.Maliev.Web.Application/Pricing/ShippingCalculator.cs': '100644 blob 8a6ce21364fec409bcabcdd70a1b76e51db960aa',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/AdmittedGltfMeshReader.cs': '100644 blob 607f43f8801e7b692ecfc8f0846c898b9caf3c99',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/AdmittedObjMeshReader.cs': '100644 blob 3d49e130d419ec6d1cc4f7bfb8b40f705030d187',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/AdmittedStlMeshReader.cs': '100644 blob e09aef2c8a7d1c3d72c751c35ce4ddd1afe52ca3',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/AdmittedThreeMfMeshReader.cs': '100644 blob 9b36c2a25c3d9e4ddbce2fa9c1e764a8dda9f580',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/FdmSimulationEngine.cs': '100644 blob c46ac966a707fcf4ebbe3c3aaa46caac6aee674e',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/FeatureMotionEstimator.cs': '100644 blob f7631ca3b22378afd062147e25ae1b005d2bddd3',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/InfillPathPlanner.cs': '100644 blob cd1fea5f27db125a10335aa10239b205a95bba9c',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/LayerContourBuilder.cs': '100644 blob 4025d6fad15369aaf83287f5ab6aed394964669f',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/MachineScenarioPlanner.cs': '100644 blob 782c68a550e68fce1bdb380b80552f5f871c4bc6',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/MeshNormalizer.cs': '100644 blob 2e19781fe6961ae48b92ac54ab7e3e5d52b34097',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/ModelPathPlanner.cs': '100644 blob 116cd402ca1de0aceb522314eafd3d6b30ba1131',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/OrientationPlanner.cs': '100644 blob 7fe5bff7482a4bc93d3ceb976b1f06b87472b9ad',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/ResolvedSimulationProfile.cs': '100644 blob 654c99db615a024f93b0ee38a42e332f3627d816',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/SimulationContracts.cs': '100644 blob 706623fec2378e236735d3ea381217a5865150db',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/SupportPlanner.cs': '100644 blob ed7deb2b4cc06e6a16a564876a5fb631b4a5bc45',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/TreeSupportPlanner.cs': '100644 blob 27ec836d7aea647d9ac0b7743b3c55c98713868e',
 'Legacy.Maliev.Web.Application/Pricing/Simulation/VoxelReconstruction.cs': '100644 blob 572a2e0d7dbad2b1839d591aa8d43b3f9281b486',
 'Legacy.Maliev.Web.Application/QuotationContracts.cs': '100644 blob b7ab18e3490251173bf547bf6a8a529489fb9afb',
 'Legacy.Maliev.Web.Application/ServiceEndpoints.cs': '100644 blob a9f734bc175e6327ded33c69df730326937c5842',
 'Legacy.Maliev.Web.Application/SocialNetworks.cs': '100644 blob 551283b2049519e3cc065a63b6b15939f80cf518',
 'Legacy.Maliev.Web.Application/ThaiLookupContracts.cs': '100644 blob 55d2e0d7aa5ef07f225f9b696ac6c58d7c85fd0d',
 'Legacy.Maliev.Web.Infrastructure/AccountSessionManager.cs': '100644 blob 4b9b4b1b00ba5e2aa37087bc8e6f043585d4110c',
 'Legacy.Maliev.Web.Infrastructure/AccountSessionStore.cs': '100644 blob ec98e1da21a0ff149b15bfcba9d8508dd17931dd',
 'Legacy.Maliev.Web.Infrastructure/AdditiveQuotationNotificationComposer.cs': '100644 blob 82ae661cce501e82a92e68a0fd6a3d0149a972a7',
 'Legacy.Maliev.Web.Infrastructure/CareerClient.cs': '100644 blob 06eba04cecc29a91186044efa0ad4c00844ead0a',
 'Legacy.Maliev.Web.Infrastructure/CareerOfferPageReader.cs': '100644 blob 2339fd9dcc63863d2a30f4ba09da98dc697404c4',
 'Legacy.Maliev.Web.Infrastructure/CncFileFinalizationClient.cs': '100644 blob d7ca079786534d8fab27d279012cb4a1cb2b780d',
 'Legacy.Maliev.Web.Infrastructure/CncFileTransport.cs': '100644 blob ab80890c627297ab2297ef3cbd0d5a5dd46f558b',
 'Legacy.Maliev.Web.Infrastructure/CncProfilePersistenceClient.cs': '100644 blob 86b16e7123a8fe5a2317adb236029cc2f1b22fb7',
 'Legacy.Maliev.Web.Infrastructure/CncRequestClient.cs': '100644 blob 2771dd4b80ca59130c73aa0a7e9b3713180d6889',
 'Legacy.Maliev.Web.Infrastructure/CncSignedLinkClient.cs': '100644 blob 24a50036511e1e2b4cfdf39894449d7d1e940d68',
 'Legacy.Maliev.Web.Infrastructure/ContactClient.cs': '100644 blob a47550ef54145f3b01ca462d84432c09d9138ea9',
 'Legacy.Maliev.Web.Infrastructure/ContactTrustedCustomerLoader.cs': '100644 blob 7df88f6c22ed44ed51b23ed31398711f90be655e',
 'Legacy.Maliev.Web.Infrastructure/CountryClient.cs': '100644 blob e8c8da42f5c5fad3a51026f5b86242bd99baa6eb',
 'Legacy.Maliev.Web.Infrastructure/CustomerAccountClient.cs': '100644 blob 05ce629a640b7ceb5517947c3eb9d6cc1996cddd',
 'Legacy.Maliev.Web.Infrastructure/CustomerAuthenticationClient.cs': '100644 blob 5b70d60c58562ac9ebbc3dd19b542baacdad0b3d',
 'Legacy.Maliev.Web.Infrastructure/CustomerDocumentClient.cs': '100644 blob 8b40b6473e43ef9a236f63eb14bd144860a2de91',
 'Legacy.Maliev.Web.Infrastructure/CustomerEmailChangeWorkflow.cs': '100644 blob 177379194f017413f73b6727aa65f4f9baacc9d4',
 'Legacy.Maliev.Web.Infrastructure/CustomerMemberDetailClient.cs': '100644 blob 2c5df09cd7c13890f54183826a5a6d17f06c8bd4',
 'Legacy.Maliev.Web.Infrastructure/CustomerOrderCatalogClient.cs': '100644 blob 08828180bff129552d6c9cb30afefa8f86457182',
 'Legacy.Maliev.Web.Infrastructure/CustomerOrderClient.cs': '100644 blob 2a0d7c033a4e218feb90d0307d1f6ef80b96aebc',
 'Legacy.Maliev.Web.Infrastructure/CustomerOrderSubmissionTransport.cs': '100644 blob 9e476ede95a568b869c06198cff99e84002f587a',
 'Legacy.Maliev.Web.Infrastructure/CustomerProfileClient.cs': '100644 blob ae470d16efc3a18351bee6ba24329da0458a113a',
 'Legacy.Maliev.Web.Infrastructure/CustomerQuotationClient.cs': '100644 blob 6cd2da46b94317d91782944a617d2bb11f8107b5',
 'Legacy.Maliev.Web.Infrastructure/GoogleMapsOptions.cs': '100644 blob cc578c7f6c500f2c0c76d9360bdebdfe34020692',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationExchangeRateClient.cs': '100644 blob 0aab8b5782762e361044e3bec276de71a9b9b9f0',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationFileCapabilityStore.cs': '100644 blob 3774cc60c0c7312c4823d10170e8464d3710b87d',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationFileServiceTransport.cs': '100644 blob 87ece0c92be8e067ab5bea8f1e4eede595085950',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationFileServiceUploadClient.cs': '100644 blob d622df474af9354fd601bd4d77afc588601bc508',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationFileUploadLimits.cs': '100644 blob 33777ffedac19d899d3d13d9d0e3da5023da02f8',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationFulfillmentClient.cs': '100644 blob b018a20a72be29acd8089350ad5bf41fcdcd11b9',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationNotificationPreparationClient.cs': '100644 blob 4cbada0d84fed7256a426f7a03cc8640ec458b62',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationPhysicalAnalysisInputReader.cs': '100644 blob 743c20db283260f3e9b6fc2aa999ff4857b97425',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationProfileCompletionClient.cs': '100644 blob 164e6baa8464d37e1bf578f12f2b578e60a825bd',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationRequestFileClient.cs': '100644 blob bc732da23d19ecca436782edf56b2f4b37ee9d95',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationSessionStore.cs': '100644 blob b31ac9a4fa5d75d4b09c15eeda5057e75f701f74',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationSubmissionStore.cs': '100644 blob 1cdd27921c056513f2a2f590a72644989632de4e',
 'Legacy.Maliev.Web.Infrastructure/InstantQuotationUploadClient.cs': '100644 blob e8b51064d83388fc4dccf6e35b603d75fcadc806',
 'Legacy.Maliev.Web.Infrastructure/Legacy.Maliev.Web.Infrastructure.csproj': '100644 blob 0b0edeb90f887078020f78420a92ea4017a93d67',
 'Legacy.Maliev.Web.Infrastructure/LineLiffOptions.cs': '100644 blob a179e9744be9430cfdafc012aa7c7d958dc5ddb0',
 'Legacy.Maliev.Web.Infrastructure/NotificationClient.cs': '100644 blob b9b5161f8f56bbf5ce474cd5f952e82ff32fe5db',
 'Legacy.Maliev.Web.Infrastructure/QuotationClient.cs': '100644 blob f3e2e45847e303bfdf2eb9c68f7b20632ea8c1f7',
 'Legacy.Maliev.Web.Infrastructure/QuotationFileClient.cs': '100644 blob cf7c89bcda262f004f410fd90b77626ba996ac86',
 'Legacy.Maliev.Web.Infrastructure/RecaptchaEnterpriseOptionsValidator.cs': '100644 blob b64056d59371271be91b57dd67b79ecc0f4f0f94',
 'Legacy.Maliev.Web.Infrastructure/RecaptchaEnterpriseVerifier.cs': '100644 blob f876c952743c8abb2856937e5fd64dae21cfe3b2',
 'Legacy.Maliev.Web.Infrastructure/ServiceAccessTokenProvider.cs': '100644 blob 51ea4e3930270fdfc35576f37c281852e2734a16',
 'Legacy.Maliev.Web.Infrastructure/ServiceCollectionExtensions.cs': '100644 blob bfcc68ac9f554bb2a653f22ab6df0835dca625bd',
 'Legacy.Maliev.Web.Infrastructure/ThaiLookupClient.cs': '100644 blob e687884874140ce6da989282e04d9ac2dee6b7b6',
 'Legacy.Maliev.Web.Tests/AboutPageContractTests.cs': '100644 blob 6fe1fd71e5abc6b6e3e9dd24412336602b07e152',
 'Legacy.Maliev.Web.Tests/AboutStaticSsrRouteTests.cs': '100644 blob 6bc2c44909b0beb950ccd1db3c13b253750a28e4',
 'Legacy.Maliev.Web.Tests/AboutStoryBrowserTests.cs': '100644 blob 140bc76089de3d3457292737a8ced59727b854ee',
 'Legacy.Maliev.Web.Tests/AccessDeniedStaticSsrRouteTests.cs': '100644 blob 83a6856299ad1206ede105000d07c021e33fd3cf',
 'Legacy.Maliev.Web.Tests/AccountAuthParityTests.cs': '100644 blob 854073eca093d1094c98d601d9b69fd4c8f0dbc8',
 'Legacy.Maliev.Web.Tests/AccountClientTests.cs': '100644 blob 3ef3fad2b3e7815289b9ea8868a24da4fa41ac6f',
 'Legacy.Maliev.Web.Tests/AccountIndexStaticSsrRouteTests.cs': '100644 blob 95dda10127cef3383b56235f475aaeff8c6df694',
 'Legacy.Maliev.Web.Tests/AccountNoindexHeaderHttpTests.cs': '100644 blob cb12b467b720eb646b251106417b84817a3398d9',
 'Legacy.Maliev.Web.Tests/AccountSessionStoreTests.cs': '100644 blob 8a42e1c99c920046c9188889370a4d9cdb150b95',
 'Legacy.Maliev.Web.Tests/AccountUtilityIndexingHttpTests.cs': '100644 blob 6a44bffbbdd178120e597bcb79debe3199fc1210',
 'Legacy.Maliev.Web.Tests/AdditiveBenchmarkTests.cs': '100644 blob 4bcbbada4a34723279d4a4aac3b2202be1207044',
 'Legacy.Maliev.Web.Tests/AdditiveCommercialEconomicsTests.cs': '100644 blob e3dac6d867bdeabeaa6efdfbf636998b9c886f67',
 'Legacy.Maliev.Web.Tests/AdditiveFxHandlerContractTests.cs': '100644 blob 065cab9a21e263ba91bb8634c0593ec6921f7cec',
 'Legacy.Maliev.Web.Tests/AdditiveGeometryValidatorTests.cs': '100644 blob 3b6f6d27d155cf77e46841a5d28278d4587f675f',
 'Legacy.Maliev.Web.Tests/AdditiveLeadTimeCalculatorTests.cs': '100644 blob b6956b733a9cad786ae34d612cf657d528945601',
 'Legacy.Maliev.Web.Tests/AdditiveOrderCostCalculatorTests.cs': '100644 blob b99312bc2e9bb414372237467c3521611a7266ac',
 'Legacy.Maliev.Web.Tests/AdditivePhysicalFailureMeaningContractTests.cs': '100644 blob 38dacb7bb2a6cfe48872d5593b94a3a474af055f',
 'Legacy.Maliev.Web.Tests/AdditiveQuotationNotificationComposerTests.cs': '100644 blob 293f283fa9323da6c8d272950b6b674b2df879c7',
 'Legacy.Maliev.Web.Tests/AdditiveQuoteTicketServiceTests.cs': '100644 blob cfc06a9748736fac8d82a6fddcbfef79d3c8f9ed',
 'Legacy.Maliev.Web.Tests/AdmittedGltfMeshReaderTests.cs': '100644 blob 45ecc2431bc5ab9bb8c0e9de856ea6c86e77b46e',
 'Legacy.Maliev.Web.Tests/AdmittedObjMeshReaderTests.cs': '100644 blob 66a18634d29027da814c6993c458693bde53e0bc',
 'Legacy.Maliev.Web.Tests/AdmittedThreeMfMeshReaderTests.cs': '100644 blob a431d1c83d5872c3192da4da1bde25199a801dd1',
 'Legacy.Maliev.Web.Tests/AnalyticsSurfaceContractTests.cs': '100644 blob f3270081138dc170d5ab97b57920aacc575ba1b1',
 'Legacy.Maliev.Web.Tests/AppSettingsCredentialBoundaryTests.cs': '100644 blob ad705a4cd067e77ed037a756de2f25fb4c79ed41',
 'Legacy.Maliev.Web.Tests/ArchitectureTests.cs': '100644 blob e4e087d8b316d02b1a10e6adbd59fedf4894cc11',
 'Legacy.Maliev.Web.Tests/AssetDeliveryContractTests.cs': '100644 blob 762f19d494eff01a5bb52e92fcbdd6785a8ac77e',
 'Legacy.Maliev.Web.Tests/AuthenticatedConsentNavigationBrowserTests.cs': '100644 blob f4773bb28d96ded9cc8837ee51aa1322e0b3fc00',
 'Legacy.Maliev.Web.Tests/AuthenticatedMemberCrawlPolicyHttpTests.cs': '100644 blob aaee299587cec11fda8be49a2bd10fc1ec44cb2d',
 'Legacy.Maliev.Web.Tests/BambuStudioProfileResolverTests.cs': '100644 blob 2d71d270079f934b3cb3db95e6cd1d1a503e6428',
 'Legacy.Maliev.Web.Tests/BankPaymentLocalizationTests.cs': '100644 blob f06bbd56b51405158b9e0a705fc7ad5a875303ee',
 'Legacy.Maliev.Web.Tests/BillingInitialProbeDiagnostics.cs': '100644 blob 401cf9dfa13515bbb951bda86a9a6fb1c23d4b7f',
 'Legacy.Maliev.Web.Tests/BillingInitialProbeDiagnosticsTests.cs': '100644 blob df1ccf2a134047fd78c5c7b5f6a475e8a9c49325',
 'Legacy.Maliev.Web.Tests/BillingProofLifetime.cs': '100644 blob bc8eb80ca2c971db63a6f46a5eb58adb91ed274a',
 'Legacy.Maliev.Web.Tests/BillingProofLifetimeTests.cs': '100644 blob da3b7fdb2f6e8892e6ac9019b188fa5dcba7d34a',
 'Legacy.Maliev.Web.Tests/BlazorHostFoundationTests.cs': '100644 blob 1b733affc341d1f3ab985bed0c15cbfcb2211279',
 'Legacy.Maliev.Web.Tests/BlazorMigrationContractTests.cs': '100644 blob b6ef62f866d3a8d17d6106f97d1d95bd56a181d9',
 'Legacy.Maliev.Web.Tests/BrowserFailureEvidence.cs': '100644 blob 90f4591eeb6047dac87a3eb7443e360df7fcfc0b',
 'Legacy.Maliev.Web.Tests/BrowserFailureEvidenceTests.cs': '100644 blob 6a526dc0f729b70de5291bdc62bc56cb86331250',
 'Legacy.Maliev.Web.Tests/BrowserHostIdentityTests.cs': '100644 blob 19e7e793c8e6240f8ab6a62b9d6fcca87aba5dbb',
 'Legacy.Maliev.Web.Tests/BrowserTestServerPort.cs': '100644 blob 118995785f24e72dc272ceaf0f89241a1253625d',
 'Legacy.Maliev.Web.Tests/BuildIdentityContractTests.cs': '100644 blob c38cc29b1879047413afdf35a8d4f543e339aded',
 'Legacy.Maliev.Web.Tests/CareerClientTests.cs': '100644 blob 33fe3900c9754cd69f6bdf1eb6b832b4d0782457',
 'Legacy.Maliev.Web.Tests/CareerDetailLevelCompatibilityTests.cs': '100644 blob 83f91427a465b8c420d07c305ad15c60035be256',
 'Legacy.Maliev.Web.Tests/CareerDetailStaticSsrRouteTests.cs': '100644 blob 4d67466ad88e15a21c8706f04b321c4730f9d870',
 'Legacy.Maliev.Web.Tests/CareerIndexStaticSsrRouteTests.cs': '100644 blob 9f1177255b756f8e7970fd9de7351a52e26dc243',
 'Legacy.Maliev.Web.Tests/CareerPageCountCompatibilityTests.cs': '100644 blob 573a2bba9e51c07ced38a2ec7c0ef0dba5526cca',
 'Legacy.Maliev.Web.Tests/ChangeEmailConfirmationStaticSsrRouteTests.cs': '100644 blob dd2999beafac58ef2361050a4998578ed2b3323b',
 'Legacy.Maliev.Web.Tests/ChannelQualityMeasurementContractTests.cs': '100644 blob 7ae3f6bb90b9b92d4904a844f73b84171c57f1c5',
 'Legacy.Maliev.Web.Tests/CncAuthenticatedProfileLoaderTests.cs': '100644 blob be97797821bcf397417cc3843bbad675d1bc0eab',
 'Legacy.Maliev.Web.Tests/CncAuthenticatedProfileTests.cs': '100644 blob 45264d19a43e7a041fe867a25a5d76449344d716',
 'Legacy.Maliev.Web.Tests/CncCommercialCalibrationTests.cs': '100644 blob 7b33a2d2a28275cda7244b4929714d1d62e4fb6b',
 'Legacy.Maliev.Web.Tests/CncDeliveryCarrierPricingTests.cs': '100644 blob 0625fa19d93d5fa885467adf467d7db40593884a',
 'Legacy.Maliev.Web.Tests/CncDeliveryPolishBrowserTests.cs': '100644 blob 6f422bce6f7f35f2a39e98f37d6c94d6164df73c',
 'Legacy.Maliev.Web.Tests/CncFileAdmissionValidatorTests.cs': '100644 blob 0ea6fc6c832ef49021653d2a55fb9ba54c71ead4',
 'Legacy.Maliev.Web.Tests/CncFileFinalizationClientTests.cs': '100644 blob 52b79d6668b96ae4b02ef015c61a666e53611d00',
 'Legacy.Maliev.Web.Tests/CncFileTransportTests.cs': '100644 blob 49bcc86f55b31b64576d4283721798221ac867e5',
 'Legacy.Maliev.Web.Tests/CncInstantQuotationPageTests.cs': '100644 blob 6d9e81c4133a6f85a32a0aa5de0d9cadea47290b',
 'Legacy.Maliev.Web.Tests/CncLiteralProducerBoundaryTests.cs': '100644 blob c6e3d073ec070bbdc7b83aacb539f053a9f8d431',
 'Legacy.Maliev.Web.Tests/CncMachiningPartGalleryTests.cs': '100644 blob ba7d4cb8eb710f9c2d9aafc0b60385226ff0346a',
 'Legacy.Maliev.Web.Tests/CncMachiningStaticSsrRouteTests.cs': '100644 blob 99412144ef376774b594452d192317a2ccd5abe2',
 'Legacy.Maliev.Web.Tests/CncNativeImportBrowserTests.cs': '100644 blob aa1c3126454f710f3563027587f95ac3df52c3dc',
 'Legacy.Maliev.Web.Tests/CncNotificationComposerTests.cs': '100644 blob 29105315ee683f8d0d0cb1a0c09fc9b2fc7bbbbe',
 'Legacy.Maliev.Web.Tests/CncNotificationCoordinatorTests.cs': '100644 blob 44e08ff9c6db570b48c0ebd6b3c8f7620acf8010',
 'Legacy.Maliev.Web.Tests/CncOwnedResourceScope.cs': '100644 blob b0beae2bf682d0b54b54667009495b6f2c4560ea',
 'Legacy.Maliev.Web.Tests/CncOwnedResourceScopeTests.cs': '100644 blob ee47f84dd7d74db6011028a1e3983cee859b1aac',
 'Legacy.Maliev.Web.Tests/CncPlannerSourceBlobParityTests.cs': '100644 blob 4d8e6e0ba81f9074ca0c4e43099f1dca975e4a74',
 'Legacy.Maliev.Web.Tests/CncProfilePersistenceClientTests.cs': '100644 blob 0cb8498226bd5d4d31ee0f0754b90d2b43d0b8f9',
 'Legacy.Maliev.Web.Tests/CncProtectedUploadBindingsTests.cs': '100644 blob cb275a5cb52e949fd5881a4836f7930248464005',
 'Legacy.Maliev.Web.Tests/CncQuotationAvailabilityTests.cs': '100644 blob a24da430080f3f1f972cf079812ccfca05b99964',
 'Legacy.Maliev.Web.Tests/CncReceiptClaimCoordinatorTests.cs': '100644 blob bd76662f7da605805ebe946a82359ef48b3646d4',
 'Legacy.Maliev.Web.Tests/CncRequestClientTests.cs': '100644 blob 22fb6c4ea20342623505fc3d1cbbdd993b93f727',
 'Legacy.Maliev.Web.Tests/CncSignedLinkClientTests.cs': '100644 blob 5c72d9272b5505f039abb0024e39e3d5cd6a464d',
 'Legacy.Maliev.Web.Tests/CncSubmissionAdmissionTests.cs': '100644 blob d03224fed0385b50073b6c388468f40c7f720a0b',
 'Legacy.Maliev.Web.Tests/CncSubmissionFormBinderTests.cs': '100644 blob ae14fbbcb6a46108ea70ccdd12349412337a8173',
 'Legacy.Maliev.Web.Tests/CncSubmissionPersistenceCoordinatorTests.cs': '100644 blob 5abb25723a8b707fd81dd98216d63605a6ce2e01',
 'Legacy.Maliev.Web.Tests/CncSubmissionRouteContractTests.cs': '100644 blob aa43146b29642f1655f6e318e99cc29b1f8eb653',
 'Legacy.Maliev.Web.Tests/CncSubmissionSnapshotTests.cs': '100644 blob 75ee73d6d8d0a9285d3c88224b0bbfe0f4cdfc0a',
 'Legacy.Maliev.Web.Tests/CncUploadAdmissionMiddlewareTests.cs': '100644 blob 460ea46bf80fd56a8e9e830145afd45c77135145',
 'Legacy.Maliev.Web.Tests/CncUploadHandlerTests.cs': '100644 blob a67b566593d1d30ccb00832769ae107904a5dfab',
 'Legacy.Maliev.Web.Tests/CncUploadReceiptStoreTests.cs': '100644 blob c58677ba87cf61ee877893f37a965e658aa90f70',
 'Legacy.Maliev.Web.Tests/CncUploadRouteTests.cs': '100644 blob 6ff7e809b783624ceec89861ae65d72a6a20d1ba',
 'Legacy.Maliev.Web.Tests/CompleteSourceHistoryParityManifestTests.cs': '100644 blob 64e0df288165d8f6e7a50f46ac629ea5936475e3',
 'Legacy.Maliev.Web.Tests/ContactClientTests.cs': '100644 blob e3c4e7b9527a8a2103de6f9a2edc6d453a4645af',
 'Legacy.Maliev.Web.Tests/ContactGoogleMapsParityTests.cs': '100644 blob 9583e309b135ebc229ec0d4936e030cf9c0ba33f',
 'Legacy.Maliev.Web.Tests/ContactPageTests.cs': '100644 blob 1d9f22eeac86ba00bffdd92c809af53af0f60861',
 'Legacy.Maliev.Web.Tests/ContactStaticSsrRouteTests.cs': '100644 blob 10945a7edf59f80cc3894a7619bde9957311c317',
 'Legacy.Maliev.Web.Tests/ContactTrustedCustomerLoaderTests.cs': '100644 blob 703c259088deb7f9cb25cae6149c3824f0e2bbf0',
 'Legacy.Maliev.Web.Tests/CountrySourceFailureObservationTests.cs': '100644 blob 8d13c4678cba7a633d9ea62262ad0a2e0b4af817',
 'Legacy.Maliev.Web.Tests/CrawlableEmailFallbackBrowserTests.cs': '100644 blob 91141bdd92db20bb8b4d6d5fa744ab850ee4410c',
 'Legacy.Maliev.Web.Tests/CrawlableEmailFallbackHttpTests.cs': '100644 blob 86eabb1f16cd581d4ed3290cd07c366d355cf74f',
 'Legacy.Maliev.Web.Tests/CustomManufacturingParityTests.cs': '100644 blob 2347bfc4233f2d03c1883e6fb6d57f42abbf9598',
 'Legacy.Maliev.Web.Tests/CustomManufacturingStaticSsrRouteTests.cs': '100644 blob 6d4f1874bce218979c371f66cb8b0870cc2c6140',
 'Legacy.Maliev.Web.Tests/CustomerAccountClientTests.cs': '100644 blob 0893668b6dfba758e76526dbbdf7fb3ee1a8234e',
 'Legacy.Maliev.Web.Tests/CustomerDocumentPagePartition.cs': '100644 blob be32686c5eb4a3f86ef336983a0cd49d6e305967',
 'Legacy.Maliev.Web.Tests/CustomerEmailChangeWorkflowTests.cs': '100644 blob 93a73ac20f268b7bf91999bd61818743b7ced57a',
 'Legacy.Maliev.Web.Tests/CustomerMemberDetailClientTests.cs': '100644 blob 683ed6961c3d56b9fa50139fe30bc07e5ac73d36',
 'Legacy.Maliev.Web.Tests/CustomerOrderCatalogClientTests.cs': '100644 blob 9312b31b72d4f2f5e749590f7efb6baa2012629c',
 'Legacy.Maliev.Web.Tests/CustomerOrderClientTests.cs': '100644 blob eb6b212d42b3be7a0c0c420d4719cbd89225c0c6',
 'Legacy.Maliev.Web.Tests/CustomerOrderSubmissionServiceTests.cs': '100644 blob f821f1727171eefcf641778ccb5b75f47dc98ed4',
 'Legacy.Maliev.Web.Tests/CustomerOrderSubmissionTransportTests.cs': '100644 blob 236bd4b66f5c560c527fedeb43c5e8374e64307d',
 'Legacy.Maliev.Web.Tests/CustomerOrderSurfaceContractTests.cs': '100644 blob c9e1b978701c672918b8fd01c9414aa34014e0a1',
 'Legacy.Maliev.Web.Tests/CustomerProfileClientTests.cs': '100644 blob b54f6f975d2311e65f4eb2aa40d306090d97bd25',
 'Legacy.Maliev.Web.Tests/CustomerQuotationClientTests.cs': '100644 blob fd8ff99e92e3ef88671d09a4a4c18e26a4081cfe',
 'Legacy.Maliev.Web.Tests/CustomerQuotationSortQueryTests.cs': '100644 blob 92540efd6ed971fd09eb51c25b14775988681886',
 'Legacy.Maliev.Web.Tests/CustomerQuotationSurfaceContractTests.cs': '100644 blob d927a09e6dd899e0bc2bba44cd00af9550325a33',
 'Legacy.Maliev.Web.Tests/CustomerSelfIdentityClientTests.cs': '100644 blob ffce57753b703c7d95a899d181470858cc7865de',
 'Legacy.Maliev.Web.Tests/DependabotPolicyContractTests.cs': '100644 blob b55efac6b661abd9771ccf54de18b6f52b0a7f00',
 'Legacy.Maliev.Web.Tests/EmailConfirmationStaticSsrRouteTests.cs': '100644 blob a5fff0d5f7e47f66fec6d9838d37ea1b38af4432',
 'Legacy.Maliev.Web.Tests/ErrorBrowserTests.cs': '100644 blob 42e9ec0982d04208b419848487f470b00379bc3e',
 'Legacy.Maliev.Web.Tests/ErrorIncidentCorrelationTests.cs': '100644 blob c5f5620a049c4c62fbc367d616c6aebf16b45cac',
 'Legacy.Maliev.Web.Tests/ErrorResponseContractMiddlewareTests.cs': '100644 blob 2c1119e0034b95e370f3a30d44d09acc92d88793',
 'Legacy.Maliev.Web.Tests/ErrorRoutePolicyTests.cs': '100644 blob 82de6722161cb8a17f295de99aacaa1ef61c7b92',
 'Legacy.Maliev.Web.Tests/ErrorStaticSsrRouteTests.cs': '100644 blob 48c09522bcd084acca767893df3344bec56c8d4e',
 'Legacy.Maliev.Web.Tests/EsdFulfillmentContractTests.cs': '100644 blob 6eb5700ae4a711a8bd1e1fb6609ea983cdb3ca5d',
 'Legacy.Maliev.Web.Tests/EsdMaterialDetailsBrowserTests.cs': '100644 blob 623365f020aa5c350522e703867dcee89e7a5aca',
 'Legacy.Maliev.Web.Tests/EsdMaterialProfileTests.cs': '100644 blob 2ec1844fc5801f7439cefefcc5c86fd4efeace8c',
 'Legacy.Maliev.Web.Tests/FacebookRetirementContractTests.cs': '100644 blob 27b0f9027d0148815085135ff3aa017b8dea2339',
 'Legacy.Maliev.Web.Tests/FdmQuantityMarginTests.cs': '100644 blob 5271e5b76a2f81476cd3a27eeabefe8ec85b118c',
 'Legacy.Maliev.Web.Tests/FdmRuntimeProfileCatalogTests.cs': '100644 blob dd033f76624b1a0a3caa9ca55e868035efb64a51',
 'Legacy.Maliev.Web.Tests/FdmRuntimeProfileEndpointTests.cs': '100644 blob 289741852ff4b554a20ee8cc6f80563a9b098aaa',
 'Legacy.Maliev.Web.Tests/FdmTrustedSimulationGeometryTests.cs': '100644 blob d1ca83d1314a985438d08dd9b537b12f6a6aba4a',
 'Legacy.Maliev.Web.Tests/FdmTrustedSimulationMotionTests.cs': '100644 blob feb2bcadb1d69121ed2a02416f1297af05e32317',
 'Legacy.Maliev.Web.Tests/FilamentProfileCatalogTests.cs': '100644 blob 6a00fb899c0da0685f64a5ae3774b502cceed243',
 'Legacy.Maliev.Web.Tests/FinishingColorDesignQaBrowserTests.cs': '100644 blob 605cad37071ea72b6351ece5f9af8b6ef408241e',
 'Legacy.Maliev.Web.Tests/FinishingColorParityTests.cs': '100644 blob f82dcfc37945c0b45e28a060cd546a73da3cead0',
 'Legacy.Maliev.Web.Tests/FinishingColorQuotePrefillTests.cs': '100644 blob 3013a22dbfe64aa13686f507ad0e323b1cbf3796',
 'Legacy.Maliev.Web.Tests/FontDeliveryContractTests.cs': '100644 blob ba62ee43389a534074d16460e4d98cccabacef35',
 'Legacy.Maliev.Web.Tests/ForgotPasswordStaticSsrRouteTests.cs': '100644 blob 8ace37bb8130c710dc7141d134a413a07a4dca8e',
 'Legacy.Maliev.Web.Tests/GoogleAnalyticsUserIdBrowserTests.cs': '100644 blob c3637feed2192fdaa7ef7ccad214ab857f2be619',
 'Legacy.Maliev.Web.Tests/GoogleAnalyticsUserIdTests.cs': '100644 blob 932aacb67eb37351a88d2bb89b1ffd810435b8d7',
 'Legacy.Maliev.Web.Tests/GoogleMapsConfigurationTests.cs': '100644 blob 3be2905357bc7bed3e35936fca58d736336b8577',
 'Legacy.Maliev.Web.Tests/HealthProcessIdentityContractTests.cs': '100644 blob 2267dd7f9e49e680d436c14e6fc9a2c034bd1be9',
 'Legacy.Maliev.Web.Tests/HomeStaticSsrRouteTests.cs': '100644 blob b90e35d98444fb782e2a87d5a50307e271fc4a8e',
 'Legacy.Maliev.Web.Tests/HomeStoryBrowserTests.cs': '100644 blob de746fe00fe5a5113c0eaa11ef0358600fcc6e63',
 'Legacy.Maliev.Web.Tests/HomeStoryBrowserTestsDiagnosticCaptureTests.cs': '100644 blob eb9450f30c0bd4b833462c801bafd4560087bde7',
 'Legacy.Maliev.Web.Tests/HomeStorySsrRouteTests.cs': '100644 blob f80d8a7162a759c7ff9b484f833f6d050b4af1b7',
 'Legacy.Maliev.Web.Tests/InitialPasswordMigrationTests.cs': '100644 blob b066f037438a8df821a9b485aefe0aebb78d1681',
 'Legacy.Maliev.Web.Tests/InstantQuotationAccessibilityContractTests.cs': '100644 blob a96e892b365fcc8852578939e521a70a9bdae495',
 'Legacy.Maliev.Web.Tests/InstantQuotationActionLayoutBrowserTests.cs': '100644 blob 490922ea1409e7a0c69b937098357e442c6e4e23',
 'Legacy.Maliev.Web.Tests/InstantQuotationAddressValidationTests.cs': '100644 blob 280643feafc719a8fedd9e7008af8770c1c7abc5',
 'Legacy.Maliev.Web.Tests/InstantQuotationAdmittedMeshServiceTests.cs': '100644 blob 45ad80229feae7efd79fefeefaf005304347c479',
 'Legacy.Maliev.Web.Tests/InstantQuotationAnalyticsCoreTests.cs': '100644 blob 76230f67b929d03c05f518b65a3e4302742bba40',
 'Legacy.Maliev.Web.Tests/InstantQuotationAnalyticsIntegrationTests.cs': '100644 blob 3d21756b33c162044a28ff7f31c96c5b926fdf5a',
 'Legacy.Maliev.Web.Tests/InstantQuotationAuthenticatedPreparationTests.cs': '100644 blob fd7049abc58a108c29e060416346dc97382e9672',
 'Legacy.Maliev.Web.Tests/InstantQuotationAuthenticatedProfileTests.cs': '100644 blob 756d9fe320530ad53b2536797884fc7c5b986772',
 'Legacy.Maliev.Web.Tests/InstantQuotationAutomaticDefaultAcceptanceTests.cs': '100644 blob 7750836329209282992bac749f6e6c33ff74da03',
 'Legacy.Maliev.Web.Tests/InstantQuotationBoundPhysicalAnalysisServiceTests.cs': '100644 blob 0d9cfa8ef43896fa77c2124b25e09555e7b53d88',
 'Legacy.Maliev.Web.Tests/InstantQuotationBrowserPortTests.cs': '100644 blob bffc02b9bed20b9cd2afe21becb13d711f3d50d3',
 'Legacy.Maliev.Web.Tests/InstantQuotationBuildPreferenceParityTests.cs': '100644 blob 10290837142332e42c6f177a4f1ce017195adfd6',
 'Legacy.Maliev.Web.Tests/InstantQuotationBulkSavingsBrowserTests.cs': '100644 blob 81bd7274c9dc01dd444ad2fdd048c37ec634b288',
 'Legacy.Maliev.Web.Tests/InstantQuotationBulkSavingsTests.cs': '100644 blob 1f499afc646642ade91d91752c11069d9b6b677c',
 'Legacy.Maliev.Web.Tests/InstantQuotationCompactControlReachabilityBrowserTests.cs': '100644 blob 783ef8da9d60423ecbda21e4d2e3697765868135',
 'Legacy.Maliev.Web.Tests/InstantQuotationCompactSummaryBrowserTests.cs': '100644 blob 4a801fcd54305f81a7d753e47d4c293ba386bf85',
 'Legacy.Maliev.Web.Tests/InstantQuotationCustomerCheckboxBrowserTests.cs': '100644 blob 692ed0fece3a6d2118b5a18f318f1c1a48dc78cc',
 'Legacy.Maliev.Web.Tests/InstantQuotationCustomerLayoutBrowserTests.cs': '100644 blob 23487e0a527dbf25452ad68bb29b44091dce462a',
 'Legacy.Maliev.Web.Tests/InstantQuotationFileCapabilityStoreTests.cs': '100644 blob 928debb39b57b85ef133625714503d71d86af655',
 'Legacy.Maliev.Web.Tests/InstantQuotationFileServiceTransportTests.cs': '100644 blob dc4633445c798cb4bd26fffe81e3c9ab4db95e25',
 'Legacy.Maliev.Web.Tests/InstantQuotationFileServiceUploadClientTests.cs': '100644 blob 8b4755dc1f7ea325fa68dfc1936432bdd43f978a',
 'Legacy.Maliev.Web.Tests/InstantQuotationFulfillmentCoordinatorTests.cs': '100644 blob 96b945bc04e210f912b0b8856cfa6ee9b2b958f0',
 'Legacy.Maliev.Web.Tests/InstantQuotationFulfillmentMappingTests.cs': '100644 blob b489f2f2327c453cba13294fbb9d145ff2143b25',
 'Legacy.Maliev.Web.Tests/InstantQuotationGltfUnitPolicyTests.cs': '100644 blob 32ab3e4d45ba01d9e3b0b21d7d52e42a122fab2c',
 'Legacy.Maliev.Web.Tests/InstantQuotationInteractiveRouteTests.cs': '100644 blob 0c57d0ef2d1910e3612a9b2132132644907a81cd',
 'Legacy.Maliev.Web.Tests/InstantQuotationJsAnalyticsSinkTests.cs': '100644 blob c37c4bbd495339b8420342fe84049e53a5ff01a0',
 'Legacy.Maliev.Web.Tests/InstantQuotationLocalizationParityTests.cs': '100644 blob 557d2f9cca900f09974d493e1b5d5e9429041622',
 'Legacy.Maliev.Web.Tests/InstantQuotationLocalizedValidationBrowserTests.cs': '100644 blob c6c3343aa154bf7e2bc6f37df954693a6b052faf',
 'Legacy.Maliev.Web.Tests/InstantQuotationMaterialPricingProgressTests.cs': '100644 blob 71403caa0f60f73ca58f3603af5aa01053eaa7f0',
 'Legacy.Maliev.Web.Tests/InstantQuotationMaterialPricingProgressTestsLifetime.cs': '100644 blob 0f0dd9bbbd33ebf6c7bf2bfdd8712fa53ddc312e',
 'Legacy.Maliev.Web.Tests/InstantQuotationMaterialSelectorLifetimeTests.cs': '100644 blob 5c0f9982e8df6b67b589f6d2aa48eccdc9f25582',
 'Legacy.Maliev.Web.Tests/InstantQuotationMaterialSummaryPresentationTests.cs': '100644 blob 0618102802aa931ee09becad2dc3d58057de0cb8',
 'Legacy.Maliev.Web.Tests/InstantQuotationNavigationFailureDiagnostics.cs': '100644 blob e0af712b36a43e70a9b7e4ae36042b1013de4162',
 'Legacy.Maliev.Web.Tests/InstantQuotationNavigationFailureDiagnosticsTests.cs': '100644 blob ab5e89246d14820f3efbc3f875dccf2ba2ce7c79',
 'Legacy.Maliev.Web.Tests/InstantQuotationNotificationPreparationTests.cs': '100644 blob 4df62d1573dd39e86cd4260d4d1ef6716422ddc8',
 'Legacy.Maliev.Web.Tests/InstantQuotationPageTests.cs': '100644 blob fc5c1af37625bb98cdc53ffdfab6e357d9eff404',
 'Legacy.Maliev.Web.Tests/InstantQuotationPhysicalAnalysisInputReaderTests.cs': '100644 blob 71df94734fd5310035e7a454880c351195da7154',
 'Legacy.Maliev.Web.Tests/InstantQuotationPhysicalResultRendererTests.cs': '100644 blob cdb0f6c6442ccbd559e8047a7aba954c31999173',
 'Legacy.Maliev.Web.Tests/InstantQuotationPreliminaryQuotationParityTests.cs': '100644 blob e287cb10869d947b191e80eea944256e90dfe798',
 'Legacy.Maliev.Web.Tests/InstantQuotationPreviewInitializationTests.cs': '100644 blob bcfc3c72afba69e63b6c126d3e30624d4e3cb782',
 'Legacy.Maliev.Web.Tests/InstantQuotationPricingTests.cs': '100644 blob 5c91ed9dfd73a92dc9c9e46cf414120ca8301bc9',
 'Legacy.Maliev.Web.Tests/InstantQuotationProfileCompletionClientTests.cs': '100644 blob 5cfb7e33722348f23fe3f8fa5a77ef1277fda863',
 'Legacy.Maliev.Web.Tests/InstantQuotationProfileProducerBoundaryTests.Source292.cs': '100644 blob b06d992d726824cf6e033860bf964604e88e0639',
 'Legacy.Maliev.Web.Tests/InstantQuotationProfileProducerBoundaryTests.cs': '100644 blob 822621e8d9f804e20ae0045c742314631afd8de5',
 'Legacy.Maliev.Web.Tests/InstantQuotationQuantityEditStateTests.cs': '100644 blob 3d85144ac36f71a076cf102b1dffc86a1d4eb0c7',
 'Legacy.Maliev.Web.Tests/InstantQuotationRequestFileClientTests.cs': '100644 blob 83060533e56893caf84b0aa1aa90cafde72a7992',
 'Legacy.Maliev.Web.Tests/InstantQuotationResinPresentationTests.cs': '100644 blob 39fe935ead4d3442183d759a9e313ad81f7ee476',
 'Legacy.Maliev.Web.Tests/InstantQuotationResponsiveCssTests.cs': '100644 blob febd8d5b7a5cd0f3c994f002a7548c9879fa6a36',
 'Legacy.Maliev.Web.Tests/InstantQuotationReviewCustomerTests.cs': '100644 blob 4738ce5d511a23cd39f16afa304e0c6f68c6a005',
 'Legacy.Maliev.Web.Tests/InstantQuotationReviewEditParityTests.cs': '100644 blob 14ba27bdb29f5717cbe39428cf215b5efe31ecca',
 'Legacy.Maliev.Web.Tests/InstantQuotationReviewLayoutBrowserTests.cs': '100644 blob 50993cf86f2a2ecf1fdd58806a98d9bc542bb6cb',
 'Legacy.Maliev.Web.Tests/InstantQuotationRuntimeIntegrationTests.cs': '100644 blob f1fd477036ec51d8f6b0dbbd954da85430eeef19',
 'Legacy.Maliev.Web.Tests/InstantQuotationSelectedPrintTimeTests.cs': '100644 blob dad1eb29b803d8fd5b00293592775408c7e4c070',
 'Legacy.Maliev.Web.Tests/InstantQuotationSessionStoreTests.cs': '100644 blob 2854103dd3ccab807c5a66acba4baa9c49528bc9',
 'Legacy.Maliev.Web.Tests/InstantQuotationSidebarLayoutBrowserTests.cs': '100644 blob 5bdad57bbd18a0a23780d200c52f250e315fd3e8',
 'Legacy.Maliev.Web.Tests/InstantQuotationSkinInfluenceParityTests.cs': '100644 blob 2fbae466e48fbf1eece12ef06447f44c3312a090',
 'Legacy.Maliev.Web.Tests/InstantQuotationStaticSsrRouteTests.cs': '100644 blob d351f0b71aa6b754772378c08baca69678188b6c',
 'Legacy.Maliev.Web.Tests/InstantQuotationSubmissionEndpointTests.ContactLocalization.cs': '100644 blob 040c490f0c8421d21cc21a7d722e1372e80a76cb',
 'Legacy.Maliev.Web.Tests/InstantQuotationSubmissionEndpointTests.CountryLocalization.cs': '100644 blob d43b7f774247e6260abb7a7b9a11c98a46026bc3',
 'Legacy.Maliev.Web.Tests/InstantQuotationSubmissionEndpointTests.StoredShipping.cs': '100644 blob ce9805fc88fb780a558af394b73dbb9225051c9c',
 'Legacy.Maliev.Web.Tests/InstantQuotationSubmissionEndpointTests.cs': '100644 blob a66f8d0ed1430b0cadfc6674dfd922e72a6c66ce',
 'Legacy.Maliev.Web.Tests/InstantQuotationSubmissionStoreTests.cs': '100644 blob fbef447eeeec1b15bba75d2696ae589343bcfc42',
 'Legacy.Maliev.Web.Tests/InstantQuotationSubmissionTests.cs': '100644 blob a71a61469b3be137dd6dda0b3f7693fb2984e280',
 'Legacy.Maliev.Web.Tests/InstantQuotationSummaryTransitionBrowserTests.cs': '100644 blob a436e13e6e320c4643f53f843fcf23aa1bce30be',
 'Legacy.Maliev.Web.Tests/InstantQuotationTerminalPhysicalFailureBrowserTests.cs': '100644 blob 2e12a1a0d676a5f822094e9524c7a98977c61e23',
 'Legacy.Maliev.Web.Tests/InstantQuotationThicknessOrderingTests.cs': '100644 blob 9b11ca4c22d41b351c7ec39a430ab6a7ff63d1ae',
 'Legacy.Maliev.Web.Tests/InstantQuotationUploadClientTests.cs': '100644 blob 358ac949fe598e429194beae91b3aea1af3e5edc',
 'Legacy.Maliev.Web.Tests/InstantQuotationWallThicknessAdmittedWorkerBrowserTests.cs': '100644 blob 4d711cc957615d85b0ff33b84c59cff6d5500878',
 'Legacy.Maliev.Web.Tests/InstantQuotationWallThicknessBrowserTests.cs': '100644 blob 725830cbb5936b1cf2d4a2d5c991ec7d973c21ce',
 'Legacy.Maliev.Web.Tests/InstantQuotationWallThicknessParityBrowserTests.cs': '100644 blob 22558ed77b6728da3135d8eb19d9fcb4f259a6f7',
 'Legacy.Maliev.Web.Tests/InstantQuotationWallThicknessRealUploadBrowserTests.Batch.cs': '100644 blob dc53867ca90d31e27069fd6613ccfc38d8e81727',
 'Legacy.Maliev.Web.Tests/InstantQuotationWallThicknessRealUploadBrowserTests.RetainedError.cs': '100644 blob '
                                                                                                 'ad28d6b167720184a83bfc12969e8d3f76d9932f',
 'Legacy.Maliev.Web.Tests/InstantQuotationWallThicknessRealUploadBrowserTests.Source292.cs': '100644 blob e87e06b9e9d1c1d51e2080a44b53d0886034790f',
 'Legacy.Maliev.Web.Tests/InstantQuotationWallThicknessRealUploadBrowserTests.Source292Aggregate.cs': '100644 blob '
                                                                                                      '435744119fc07a24175a08160bd241db17f034cb',
 'Legacy.Maliev.Web.Tests/InstantQuotationWallThicknessRealUploadBrowserTests.Units.cs': '100644 blob 9812e88dc45d459adc146f7fd244c4d2692f7c9d',
 'Legacy.Maliev.Web.Tests/InstantQuotationWallThicknessRealUploadBrowserTests.cs': '100644 blob 7f9ffed6d56d701ab9216c178824606295b4a51f',
 'Legacy.Maliev.Web.Tests/InstantQuotationWorkflowInteropTests.cs': '100644 blob 1672a2e2b0c9222e37b7ba5e6bd697ecb224df43',
 'Legacy.Maliev.Web.Tests/InstantQuotationWorkflowPricingTests.cs': '100644 blob 23d0cdfef545b20868ef10ec42ffe1dde4d43258',
 'Legacy.Maliev.Web.Tests/InstantQuotationWorkflowUploadTests.cs': '100644 blob 6033997a1e4e159f4668e27e297daddbc1101bb8',
 'Legacy.Maliev.Web.Tests/JavaScript/additive-client-simulation.test.cjs': '100644 blob 7f7064c0cdff290a0ad641326239c5b4c78faf8a',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-access.test.cjs': '100644 blob aabab2303c7a7716dedfffa41e6d4e839bfa04f4',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-ball-contact.test.cjs': '100644 blob 6ea312aea9dc6075eeaf7480918940976bcaaeeb',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-ball-handoff-allocation.test.cjs': '100644 blob 51b73d66cc34fa1f2b035fc05f13a798d2dad1aa',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-ball-rest-policy.test.cjs': '100644 blob c14174971032de3470303af229cfac361a34b6f5',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-bracket-operations.test.cjs': '100644 blob 995618df2a6596cd8f2f55df609fc8c662449f79',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-cad-surfaces.test.cjs': '100644 blob 5cadc945c7d017d5eebbb28f049889558319ca78',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-circular-refinement.test.cjs': '100644 blob 2dafbdbf0bcd96406d3f5d1062259e6eaa9d817b',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-comparison-pricing.test.cjs': '100644 blob f66e33c8e1d4b270f985fee359110d1c13f7f637',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-contour-evidence.test.cjs': '100644 blob 34404357edd6ef94f4767ec6c5a93b3e2f3e7514',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-curved-classification.test.cjs': '100644 blob 9643365e2fe6b50dfe93fcc3678269a7ca332b1f',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-curved-operations.test.cjs': '100644 blob 533eaffa6cce23b42e06b3dd02f90eacc02f72a3',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-curved-ownership.test.cjs': '100644 blob 70912e99dd421c3824db047974675ce9e3a29571',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-curved-review-guards.test.cjs': '100644 blob 022f71e337064386ef2e4349cdba1e021fb8b9ac',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-curved-stock-planning.test.cjs': '100644 blob dde308a79db7ecd09f1b9a7084b0a447df444229',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-cutter-contact.test.cjs': '100644 blob 67afd4d75c708dea520d59ca7d1a8217b1460cbd',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-detected-thread-requirements.test.cjs': '100644 blob c6f5ea5840c3335a420e932446afd8ea276c1272',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-distance-field.test.cjs': '100644 blob e6788b03b3e802f5062a3346ba5aa9ae5cf27160',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-feature-graph.test.cjs': '100644 blob 6d3000e03a802cf82815f4e4d0b577dc9ecb2f29',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-feature-operation-guards.test.cjs': '100644 blob 1dc18d3eea37e23f334a18920c1ad1bbdbf151b1',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-final-audit-regressions.test.cjs': '100644 blob 9223acf792a0822e5cefe7e50f4fb989e841cab4',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-general-ball-handoff.test.cjs': '100644 blob b9ddb71e643038e0ad787b2f40ecc18fb3fdf5e4',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-geometry-summary.test.cjs': '100644 blob 1bb64c7cc2dd3a3d43f1930529ada1d1de792d0e',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-linked-hole-setups.test.cjs': '100644 blob 0d39cd2366a535553829faff8c2609c603416fb4',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-local-catalog.test.cjs': '100644 blob 212a9305c45411092e7e5733963197d2b837c48b',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-local-thread-features.test.cjs': '100644 blob 562826e437f3bc6b21d787ef3ecc145948b1857a',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-manufacturing-evidence-architecture.test.cjs': '100644 blob 009d41435b2ddefab8a4c23bac2e608bbd930f9e',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-model-worker-harness.cjs': '100644 blob 60ce3ef92c2756ebc510b6beafd4055c3dae025c',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-native-dispatch.test.cjs': '100644 blob 0515a81084c6e786d8bce603455c153a06996015',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-native-interpretation.test.cjs': '100644 blob d7275b292d571802289cc6830e644e9ff056cbed',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-native-prismatic-regions.test.cjs': '100644 blob b6d88350450019b5de9946f7ac71ee28bdcb9e8a',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-native-region-fixtures.cjs': '100644 blob ac28c3971f0d5631bc77a9cd58d107a83c9b5b5b',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-native-regions.test.cjs': '100644 blob cae2ac95f0787bc850b1e82fa128b5df652e075f',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-native-runtime-loader.test.cjs': '100644 blob 2ae32a8fb620945e83c5fe28f216c5015b7b082f',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-native-thread-page-harness.cjs': '100644 blob bfdd461c08188775e733f598083bd3fea0577dd0',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-native-thread-paired-radial.test.cjs': '100644 blob c66b5c806c93ffbf8c40106965f3d3eb49c5c142',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-native-thread.test.cjs': '100644 blob cddbd634d0d7cff4296b3ac951761ceb4bf9801c',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-native-topology.test.cjs': '100644 blob 3fc6700d9cb28ac09bc93dcb41fe142e8bc76caf',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-outside-distance.test.cjs': '100644 blob bb08268efeead54458185c7a158682989337c720',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-outward-ball-contact.test.cjs': '100644 blob 9d95a605d83470a408a2894447794b2b6844171d',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-parallel-wall-contact.test.cjs': '100644 blob 84f629321eadb5ecc0b222e8f91021c0b825751e',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-plan-contracts.test.cjs': '100644 blob 220d593029e1cde8200f2d17128fcdc433ef953a',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-plan-integration.test.cjs': '100644 blob a596193219fc9483343168518d91bb36e1d9e113',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-plan-validator.test.cjs': '100644 blob 32190fbd3fce36eb28df668548efbd0f25a56813',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-process-compiler.test.cjs': '100644 blob 95593b3ba674010962e944d96ed46bcfbb8fb609',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-real-axial-acceptance.test.cjs': '100644 blob 1f3f7b33c7ec59d256e6be575f5c432d4066c01d',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-round-contact.test.cjs': '100644 blob 11e2ae8db3de9ad33d04312e26b500d8b48f9b34',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-setup-ownership.test.cjs': '100644 blob 06a26ce5075a597f54637758a57ab200cca333ba',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-setup-planner.test.cjs': '100644 blob 2999793294ed4c5c207ee3172b7444e0d4df006a',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-sloped-contact.test.cjs': '100644 blob 98273f27039d2f7d6cea32a60b42a7b0edb44f91',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-solid-field.test.cjs': '100644 blob 21429ca29a7dddf3748082e0e868e5e918da37f9',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-spot-drill-catalog.test.cjs': '100644 blob f80af2089ed8d083b172b328ea8891f0828fcc39',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-thread-extent-evidence.test.cjs': '100644 blob 538a725963f647ccd5b37516b37b28573f787f57',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-threaded-plate-operations.test.cjs': '100644 blob cf21a445df49d1f1611535ab0a4001ce4b89fda3',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-tool-coverage.test.cjs': '100644 blob 1a2dae5eaaee5900a51595174bf63abe4864b4be',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-tool-library.test.cjs': '100644 blob bbe17895c960d451b50eb30ec4bff69e3ee50762',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-topology.test.cjs': '100644 blob 649b60fdc69ff384c55c9b560692c0b0f1bc9a83',
 'Legacy.Maliev.Web.Tests/JavaScript/cnc-worker-cad-hints.test.cjs': '100644 blob 8d32c29068bc1d716b3a3ed64457ad51cc7e36e2',
 'Legacy.Maliev.Web.Tests/JavaScript/fixtures/cnc-legacy-planning.test-helper.js': '100644 blob aef1834c3671f90716baa9c79247f5899a2632c5',
 'Legacy.Maliev.Web.Tests/JavaScript/knowledge-search.test.cjs': '100644 blob 10d2fbfdc9614ac0b8dabed38c46f3978a770a61',
 'Legacy.Maliev.Web.Tests/JavaScript/material-selection-scroll.test.cjs': '100644 blob 7e66d3f92f0f9b3fbb82fa925a9ab9e5b645154a',
 'Legacy.Maliev.Web.Tests/JavaScript/model-viewer-cad-source-regression.test.cjs': '100644 blob 14541c5c3a45da123bf23a9035b8344134cc807f',
 'Legacy.Maliev.Web.Tests/JavaScript/model-viewer-processing-watchdog.test.cjs': '100644 blob 6a57a3ca9f34241f024c41a46023a105a349414d',
 'Legacy.Maliev.Web.Tests/JavaScript/model-viewer-source-worker.test.cjs': '100644 blob b78fbbee0a66330669188792f4410ca5dbef6487',
 'Legacy.Maliev.Web.Tests/JavaScript/spacemouse-bootstrap.test.cjs': '100644 blob ba085f0aebd8cc49a0d2dade8586c85d49af129a',
 'Legacy.Maliev.Web.Tests/JavaScript/spacemouse-navigation.test.cjs': '100644 blob aaf6c8c49a132d840f59f3f279d2f05399118dca',
 'Legacy.Maliev.Web.Tests/JavaScript/wall-thickness-sampling.test.cjs': '100644 blob 37964c21efea16a23c3b73212feb8800f5475bb7',
 'Legacy.Maliev.Web.Tests/JavaScript/wall-thickness.test.cjs': '100644 blob b7d8d14324375aef7b72f3c1657cb6d400e3f75f',
 'Legacy.Maliev.Web.Tests/KnowledgeCenterGuidesBrowserTests.cs': '100644 blob c54d00a14a308bf95cc6bf3b3ac14d1ee312451e',
 'Legacy.Maliev.Web.Tests/KnowledgeCncMachiningStaticSsrRouteTests.cs': '100644 blob 8a6bd8313d14457ebc4ba7d4b3e93872baac3bab',
 'Legacy.Maliev.Web.Tests/KnowledgeGroupedNavigationBrowserTests.cs': '100644 blob 159b4b6a827590536e0446dba7943da5733c6208',
 'Legacy.Maliev.Web.Tests/KnowledgeGuideBundleRouteTests.cs': '100644 blob ec901200c36552d0f4e5772a8860f894b0612167',
 'Legacy.Maliev.Web.Tests/KnowledgeGuidelinesStaticSsrRouteTests.cs': '100644 blob 9c0f252966d11cea948bfd4eea12eea91f2272d4',
 'Legacy.Maliev.Web.Tests/KnowledgeLibrarySourceContractTests.cs': '100644 blob ca0d0bb2f2574b549d460e07dca70a92a1db6ee4',
 'Legacy.Maliev.Web.Tests/KnowledgeReadingLayoutBrowserTests.cs': '100644 blob dc778964714472d8c2ffc805b5b12faa1558cae1',
 'Legacy.Maliev.Web.Tests/KnowledgeSearchBrowserTests.cs': '100644 blob 2c4dd64629ce313674b1780e2d65c8318edd2983',
 'Legacy.Maliev.Web.Tests/KnowledgeSidebarBrowserTests.cs': '100644 blob c7dc37f18960f8f09d4f8a97cdc884b6badcbb2c',
 'Legacy.Maliev.Web.Tests/KnowledgeSidebarRouteTests.cs': '100644 blob e7dd62a6612fc6ba545ea899dec6f2c0457c26ea',
 'Legacy.Maliev.Web.Tests/KnowledgeThreeDimensionalPrintingStaticSsrRouteTests.cs': '100644 blob dfa63a564b60adcfd2b4c9467e05d65a0c7f0275',
 'Legacy.Maliev.Web.Tests/KnowledgeThreeDimensionalScanningStaticSsrRouteTests.cs': '100644 blob 6af08593abcf121a1667613baf6a83cdb1688063',
 'Legacy.Maliev.Web.Tests/KnowledgeVisualAssetDeliveryTests.cs': '100644 blob 3a1f0a5736d37141c15d9f47ef42dda7757be7d5',
 'Legacy.Maliev.Web.Tests/KnowledgesIndexStaticSsrRouteTests.cs': '100644 blob 3a08a45b80efe77546ae78af9d4a227c093f0dd9',
 'Legacy.Maliev.Web.Tests/LanguageSwitchInputTests.cs': '100644 blob c0699ee5e1b9c9209159ea630502169d8569c502',
 'Legacy.Maliev.Web.Tests/LeadAnalyticsEventQueueParityTests.cs': '100644 blob 8c6019a7a08a94ee65879476cdd0f323b568b5b0',
 'Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj': '100644 blob 28cd8eb5af11657ff6cfe4c9ba6d8b384ef608b1',
 'Legacy.Maliev.Web.Tests/LegacyAccountFormSourceParityTests.cs': '100644 blob 99e8c17c4f5847c2689913d86493c741fd38e84c',
 'Legacy.Maliev.Web.Tests/LegacyCredentialRecoveryParityTests.cs': '100644 blob 3654f16beec44b05c1a86414b5728cef497204c3',
 'Legacy.Maliev.Web.Tests/LegacyMotionSourceParityTests.cs': '100644 blob 364559118590aaed7a9a4905aaaa64dde930ffd1',
 'Legacy.Maliev.Web.Tests/LegacyServiceDefaultsIdentityContractTests.cs': '100644 blob e4ac1003ebfa4b546281366828613db17b9e49d7',
 'Legacy.Maliev.Web.Tests/LegalContentParityTests.cs': '100644 blob 63cbaa9b130441685dd71f9eb69969e164a72eef',
 'Legacy.Maliev.Web.Tests/LegalIndexStaticSsrRouteTests.cs': '100644 blob e818a560574a8daba04279cb6437786fa99b6c85',
 'Legacy.Maliev.Web.Tests/LegalLocalizationContractTests.cs': '100644 blob 796f72314e16c6996a1d4ddf40b1c0b44c1effb5',
 'Legacy.Maliev.Web.Tests/LocalPerformanceContractTests.cs': '100644 blob a83c74182d774b5dc44c313addc46bac989527b3',
 'Legacy.Maliev.Web.Tests/LocalizedCanonicalCookieHttpTests.cs': '100644 blob 02f274c9ab92c8f80e21e81677af513ecccb4a62',
 'Legacy.Maliev.Web.Tests/LocalizedServiceSeoHttpTests.cs': '100644 blob 41079703ded0094d5b3163870c3972b9c34c1b1a',
 'Legacy.Maliev.Web.Tests/LoginStaticSsrRouteTests.cs': '100644 blob 2fcb562b8e454575ed362bf44726690c0056fba7',
 'Legacy.Maliev.Web.Tests/LogoutStaticSsrRouteTests.cs': '100644 blob 2d0d623da38d29b349a5b6722077d8b16ba5ad6e',
 'Legacy.Maliev.Web.Tests/LowVolumeInjectionMoldingParityTests.cs': '100644 blob 9aa642868d6bb98269feebc20dcdf9c6a14542e9',
 'Legacy.Maliev.Web.Tests/MaterialCardBrowserActions.cs': '100644 blob fc873459e19a53c14bc4bec37229cb1de97002cf',
 'Legacy.Maliev.Web.Tests/MaterialCardPresentationCatalogTests.cs': '100644 blob 52b246e99e76052850e50a2bee277ef71a1dcd89',
 'Legacy.Maliev.Web.Tests/MaterialComparisonRepricingContinuityTests.cs': '100644 blob 265fafe91e1f60618b946d4307b282bc8f2f55f4',
 'Legacy.Maliev.Web.Tests/MaterialCompletionObservation.cs': '100644 blob b01d2ca798be96065cde7d8374e6d890b0da4373',
 'Legacy.Maliev.Web.Tests/MaterialCompletionObservationForwardingTests.cs': '100644 blob 1029de043aba0042a1aa2058a867d55294425a91',
 'Legacy.Maliev.Web.Tests/MaterialInitialNavigationDiagnostics.cs': '100644 blob 4bbc4ce25a94cb9def1c1d6eaec21ca58e1347bf',
 'Legacy.Maliev.Web.Tests/MaterialInitialNavigationDiagnosticsTests.cs': '100644 blob d655fee864ae92db07789d1b9a76bcfd83c9f7b4',
 'Legacy.Maliev.Web.Tests/MaterialInitialRequestStages.cs': '100644 blob a5516916e8d0802a9c1c888325f2b820680a4351',
 'Legacy.Maliev.Web.Tests/MaterialInitialRequestStagesTests.cs': '100644 blob aa271f415bb07bb4aedf461d7935c692c90cef70',
 'Legacy.Maliev.Web.Tests/MaterialPricingAsyncCleanupTests.cs': '100644 blob b10850e20fc2d97099fb71907d2e510473a856cf',
 'Legacy.Maliev.Web.Tests/MaterialPricingDisplayBrowserPortTests.cs': '100644 blob 5c39496215ef2f2bb2f12ae09cd6434ef6bff663',
 'Legacy.Maliev.Web.Tests/MaterialPricingDisplayBrowserTests.cs': '100644 blob cfcac511a84749d129dbcea4be3f8c3856d0e7a1',
 'Legacy.Maliev.Web.Tests/MaterialPricingDisplayContractTests.cs': '100644 blob f5ef34e05e4ce81a0004b2635f11f3d37dee6b7b',
 'Legacy.Maliev.Web.Tests/MaterialPricingDisplayRendererContractTests.cs': '100644 blob 2fc226e5c4bf125094fbda84a0503e3d6a166de9',
 'Legacy.Maliev.Web.Tests/MaterialPricingLifetimeTests.cs': '100644 blob 8b91662c4216a243e13b7a69c8a144dd4b5382e6',
 'Legacy.Maliev.Web.Tests/MaterialPricingTestCleanup.cs': '100644 blob 306e7cf274cdedaf0d4535b78ea29b74407672ec',
 'Legacy.Maliev.Web.Tests/MaterialPricingTestDiagnostics.cs': '100644 blob 45ec90ee636a2ca0c8e3e3565c2c292adc5b7360',
 'Legacy.Maliev.Web.Tests/MeasurementLifecycleBrowserTests.cs': '100644 blob 05d4a298a4a2d0cb3d7a4c81823ffb45676a5356',
 'Legacy.Maliev.Web.Tests/MeasurementTempDataHandoffHttpTests.cs': '100644 blob c6777f786048c6cfa54ead0e73e0a4ea3a6986dc',
 'Legacy.Maliev.Web.Tests/MemberAddressPageTests.cs': '100644 blob 0484733055540c433b4a68db94cb1c80754c19ae',
 'Legacy.Maliev.Web.Tests/MemberAuthorityChild.cs': '100644 blob 4cf02936eed2cab63f34318b78f8d64517d6f5db',
 'Legacy.Maliev.Web.Tests/MemberAuthorityOwnershipTests.cs': '100644 blob 996bf304b10187668f5b6bde9b07e2d933637237',
 'Legacy.Maliev.Web.Tests/MemberChangeEmailPageTests.cs': '100644 blob 61b9a38367189efe563eec4e1afbcfa9bbeb02fd',
 'Legacy.Maliev.Web.Tests/MemberChangePasswordPageTests.cs': '100644 blob e350977f454231a12a4022e8620cf602f930af60',
 'Legacy.Maliev.Web.Tests/MemberCreatePasswordPageTests.cs': '100644 blob 521ed6279816662027c65fc05e72d7e3e3ef12cc',
 'Legacy.Maliev.Web.Tests/MemberDashboardPageTests.cs': '100644 blob 6d015d8edc39e9a6a938678fac058a98e87bf91a',
 'Legacy.Maliev.Web.Tests/MemberDashboardSurfaceContractTests.cs': '100644 blob c8178c0d894e5ed70096d0e373a67f6798004d73',
 'Legacy.Maliev.Web.Tests/MemberDocumentDecidedEvidenceWireTests.cs': '100644 blob 11bfb6ce0ec201bb73a13df0e413e1edf77d527e',
 'Legacy.Maliev.Web.Tests/MemberDocumentsBrowserTests.cs': '100644 blob 0a2e21dcf677bd0ab153e3b8982ae208d9753cb2',
 'Legacy.Maliev.Web.Tests/MemberDocumentsHttpTests.cs': '100644 blob 1a677321d964d466ea2e684252cb8c859c418afa',
 'Legacy.Maliev.Web.Tests/MemberOrderCompatibilityTests.cs': '100644 blob 891aecf5e3a53826e07c7d3e9cf1f2ffb4908a12',
 'Legacy.Maliev.Web.Tests/MemberOrderHistoryParityTests.cs': '100644 blob 98b6ba7ddd1ade1ac05430a5df7ffc74935d9f48',
 'Legacy.Maliev.Web.Tests/MemberOrderPostShipmentParityTests.cs': '100644 blob 3af2b67c4f8cd20395bd30b3a508f5f95fce5648',
 'Legacy.Maliev.Web.Tests/MemberOrderUploadBrowserTests.cs': '100644 blob 96981b060e4eae214c830b6b03b4872a3020db16',
 'Legacy.Maliev.Web.Tests/MemberOrderUploadLimitTests.cs': '100644 blob 04bf5162c524e0cc3a682c5bb01ed93df82cb726',
 'Legacy.Maliev.Web.Tests/MemberProfilePageTests.cs': '100644 blob ef27e936389c6e889f3eb9000bda41dd3d1edc95',
 'Legacy.Maliev.Web.Tests/MemberStaticSsrRouteOwnerTests.cs': '100644 blob 788420f98d7b509856eca703ad5383f1caa849c9',
 'Legacy.Maliev.Web.Tests/ModelViewerCadSourceBrowserTests.cs': '100644 blob a64af6b6508e7b557cde3eafb71a24be3ee5ed62',
 'Legacy.Maliev.Web.Tests/ModelViewerWorkerBrowserTests.cs': '100644 blob 0e5b6b85b8f36204190b77502c677747b384e9a6',
 'Legacy.Maliev.Web.Tests/NativeLoggingMigrationTests.cs': '100644 blob 4cc21be1518525966ff24f26037c4bbfe7fa4435',
 'Legacy.Maliev.Web.Tests/NoWeaponsStaticSsrRouteTests.cs': '100644 blob 366fcc388d39edc3c2100290cb83f941e5f6faba',
 'Legacy.Maliev.Web.Tests/NonDisclosureAgreementStaticSsrRouteTests.cs': '100644 blob 3cb7c8d5c85ebbf1303fdcef6deacc26695bf11a',
 'Legacy.Maliev.Web.Tests/NotificationClientTests.cs': '100644 blob 3f0ffc44760419ed969a103ca9a4194f3836d76e',
 'Legacy.Maliev.Web.Tests/OriginalBrowserFixtureSuccessorPortTests.cs': '100644 blob 1ed845236de7849d0f02f85744efe06d8f1903c4',
 'Legacy.Maliev.Web.Tests/OwnedProcessMetricObservation.cs': '100644 blob e2165721e7bd09273910cdcff0c559825930faeb',
 'Legacy.Maliev.Web.Tests/OwnedTestWebHostBuilder.cs': '100644 blob fc9b13b6ee3186aa111372a7add569f17b006570',
 'Legacy.Maliev.Web.Tests/OwnedWebSurfaceHost.cs': '100644 blob 3e726afbb141394afeed435dd6028e9a40c2db5e',
 'Legacy.Maliev.Web.Tests/PageSizeQueryValidationTests.cs': '100644 blob 6ee2f49f7a335f47cdc447d8ce9276a2550c08f9',
 'Legacy.Maliev.Web.Tests/Pre605/LeadAnalyticsEventQueue.cs': '100644 blob b3844b39cdb382eef8953c2852c8700ec10ce4f8',
 'Legacy.Maliev.Web.Tests/PreliminaryQuotationDurationVectorBrowserTests.cs': '100644 blob d8a7c8ba92df72543bfdcfbedd407a70865e3329',
 'Legacy.Maliev.Web.Tests/PrivacyPolicyStaticSsrRouteTests.cs': '100644 blob a615ca88ad258fa6aa59787ac5d881ff2262184d',
 'Legacy.Maliev.Web.Tests/PrivateRequestObservationConsumerTests.cs': '100644 blob 230fcc4b9fdd1236e2e7ccd3f816b656dd97d922',
 'Legacy.Maliev.Web.Tests/ProtectedGeometryProfileContinuityTests.cs': '100644 blob fb1db4fb7809358635098aaf479b9c21c5f9d3c7',
 'Legacy.Maliev.Web.Tests/PublicAccountWorkflowTests.cs': '100644 blob a0df89166e1fc0ec242bc793b5d11eb4d7f93e5a',
 'Legacy.Maliev.Web.Tests/PublicBrowserCompatibilityNoticeMigrationTests.cs': '100644 blob 21d1b153f408d1fd2cda3df273e68543c328701c',
 'Legacy.Maliev.Web.Tests/PublicBusinessStructuredDataMigrationTests.cs': '100644 blob eab6148fb0f0f78cf0ddf7dd165a3fd8815eacba',
 'Legacy.Maliev.Web.Tests/PublicContactChannelAnalyticsMigrationTests.cs': '100644 blob 6506ac92e3c89e1f7a9150b0523c6e6c5eb756ae',
 'Legacy.Maliev.Web.Tests/PublicContactChannelsParityTests.cs': '100644 blob 4216c3ccc624b0075a595173b84631a7e56286ff',
 'Legacy.Maliev.Web.Tests/PublicContactFormBrowserTests.cs': '100644 blob 54267d7d6cd451753857233f3319334888b56900',
 'Legacy.Maliev.Web.Tests/PublicContactRenderedScriptsTests.cs': '100644 blob b890e9401b969f6caa7c65909a674d50b825b663',
 'Legacy.Maliev.Web.Tests/PublicCookieConsentBrowserTests.cs': '100644 blob 32bec70d523db2d6b851db91e6624fcb848cf3b3',
 'Legacy.Maliev.Web.Tests/PublicCookieConsentMigrationTests.cs': '100644 blob ab3ba24845863caf631a35ef303b15ba9f325c8c',
 'Legacy.Maliev.Web.Tests/PublicCopyParityTests.cs': '100644 blob 4554384c3249a2bcae976cd718818c7757d06a68',
 'Legacy.Maliev.Web.Tests/PublicCrawlerInventoryAcceptanceTests.cs': '100644 blob 8cb6c2a23675f415832b9a4961378155fc715b8e',
 'Legacy.Maliev.Web.Tests/PublicDocumentLinksMigrationTests.cs': '100644 blob 55aa5e70ef364d91a9dd0f857db7516475bf90b0',
 'Legacy.Maliev.Web.Tests/PublicDocumentVerifierQuotationHttpTests.cs': '100644 blob 4d42d67611922afc92d4ed9d75b081a73bc1875b',
 'Legacy.Maliev.Web.Tests/PublicGoogleTagManagerMigrationTests.cs': '100644 blob b33acf9ea74c2c910cc4db2a1ec58503625e9dd0',
 'Legacy.Maliev.Web.Tests/PublicNavigationOwnedRoutesTests.cs': '100644 blob a4d1bb29609393a803ee330efa7061ee614bbd3c',
 'Legacy.Maliev.Web.Tests/PublicOpenGraphMetadataMigrationTests.cs': '100644 blob 20327c826593d24297321e32cf5e47a2d326c3bf',
 'Legacy.Maliev.Web.Tests/PublicServiceCoverageRenderedContractTests.cs': '100644 blob 72996d0e662cec1dfea9063b854430fd5b7bb924',
 'Legacy.Maliev.Web.Tests/PublicServiceExpansionStaticSsrRouteTests.cs': '100644 blob c7df5faa4c3a13be31da65e7a1c63963aa09fde7',
 'Legacy.Maliev.Web.Tests/PublicServiceFaqStructuredDataParityTests.cs': '100644 blob e2c9eaddfd848d2907221bed4289567a085043a2',
 'Legacy.Maliev.Web.Tests/PublicServiceStructuredDataMigrationTests.cs': '100644 blob b08594aead8547446f0035d7a96bea4876c6cedc',
 'Legacy.Maliev.Web.Tests/PublicWebpImageContractTests.cs': '100644 blob f0aa9dabe78b0ef02b541bf7ca1305ef8c582a2d',
 'Legacy.Maliev.Web.Tests/PublishWorkflowPermissionContractTests.cs': '100644 blob fd8c57d037e8373691cd31bb0c4188f9ee768643',
 'Legacy.Maliev.Web.Tests/QuotationClientTests.cs': '100644 blob 0c3278db7d0ded1b77b59830761cf8e7de079a1f',
 'Legacy.Maliev.Web.Tests/QuotationPageTests.cs': '100644 blob b8876b2abaaaea78c20c1bd0f4c2fb1f1326d4b0',
 'Legacy.Maliev.Web.Tests/QuotationRoutePolicyTests.cs': '100644 blob cbc88f5f52c84a1d4495384687729a1b90cac10b',
 'Legacy.Maliev.Web.Tests/QuotationStaticSsrRouteTests.cs': '100644 blob 65cb66b12a4040ae07ea4942b2df6c2256414c6a',
 'Legacy.Maliev.Web.Tests/RecaptchaConfigurationStartupTests.cs': '100644 blob c8b9d0ea53835a11ce391f8b2e21dde12b8ad03c',
 'Legacy.Maliev.Web.Tests/RecaptchaEnterpriseVerifierTests.cs': '100644 blob 048049e23eb4fb88738ecd75728ca795f7d07d88',
 'Legacy.Maliev.Web.Tests/RedisRuntimeIntegrationTests.cs': '100644 blob aa4ffe6a79c23a6aa1e6343a6cefad2625083ae7',
 'Legacy.Maliev.Web.Tests/RepricingActivityTests.cs': '100644 blob c25269f743becefbbf6f4ec3a997057ee8ffba77',
 'Legacy.Maliev.Web.Tests/ResetPasswordStaticSsrRouteTests.cs': '100644 blob cfa30849cc0793f5e51469c84478ef7bfee04f45',
 'Legacy.Maliev.Web.Tests/ResinBuildProfileEvidenceTests.cs': '100644 blob 279bc00e3fba29feabc04831ffe956969544ab98',
 'Legacy.Maliev.Web.Tests/ResinServerSupportEvidenceParityTests.cs': '100644 blob 145b5da58f341ae88e2ed2926bd012964c72d8c8',
 'Legacy.Maliev.Web.Tests/ResponsiveServiceImageDeliveryTests.cs': '100644 blob ffb03d3ca32b47d6c5cafe3c57548cd5ffa54d94',
 'Legacy.Maliev.Web.Tests/RouteScopedAssetRenderingTests.cs': '100644 blob 810f06218cdec6f4869388e3edabc49cd7f5a0b0',
 'Legacy.Maliev.Web.Tests/ScanningComparisonBrowserTests.cs': '100644 blob ac03c8cbc4416248b4a14738ab9985e8ee7b2939',
 'Legacy.Maliev.Web.Tests/ScanningIntentGuideHttpTests.cs': '100644 blob 2ceb023038cf6c2bb441b9ac4d49629980c265be',
 'Legacy.Maliev.Web.Tests/ScanningPresentationContractTests.cs': '100644 blob eb5b30d8a16322b976934e262c66c054a2590b8c',
 'Legacy.Maliev.Web.Tests/ScanningProofBrowserTests.cs': '100644 blob b2cc53d3a70df3fcb6fc6da8b9f543f0b5bd1a40',
 'Legacy.Maliev.Web.Tests/ScanningWorkflowSourceCommitBrowserTests.cs': '100644 blob fa76df7dfb9b817584a484559210de33ee5f3845',
 'Legacy.Maliev.Web.Tests/ScanningWorkflowSourceCommitHttpTests.cs': '100644 blob efe99b7eb50b655b9f0f6b8ca284ca27c425fc6b',
 'Legacy.Maliev.Web.Tests/ScbPaymentServiceBoundaryTests.cs': '100644 blob 5181a6f2c7648331a7390645e76998bdd9eeac47',
 'Legacy.Maliev.Web.Tests/SelectedPrintTimeTimeoutDiagnostics.cs': '100644 blob 3ad557f4ffff6a5a4abac7e871120bcacdef1521',
 'Legacy.Maliev.Web.Tests/SelectedPrintTimeTimeoutDiagnosticsTests.cs': '100644 blob 02c0f77fb0490f57dea992ec35398f66dbc75294',
 'Legacy.Maliev.Web.Tests/SeoBusinessContractParityTests.cs': '100644 blob 69f055f03e11baa1c5e2760d997aee9e74b11bd2',
 'Legacy.Maliev.Web.Tests/ServiceAccessTokenProviderTests.cs': '100644 blob 37374f8a508c3f0d7e4d8e19690dfa320070a621',
 'Legacy.Maliev.Web.Tests/ServiceFinderHandoffTests.cs': '100644 blob 904e3a933bf3ad05bed3e64d8eddf364ac2c8dcb',
 'Legacy.Maliev.Web.Tests/ServiceFinderPresentationTests.cs': '100644 blob 5bce3e6e5b8d04421790da5accce99d051dec484',
 'Legacy.Maliev.Web.Tests/ServiceGalleryHistoricalBrowserTests.cs': '100644 blob 591bee461d3e78c7ca932a8acfd4d4cfec8c7ba3',
 'Legacy.Maliev.Web.Tests/ServiceHeroFollowupContractTests.cs': '100644 blob 739287f0722f32f6594da67fd938e9ffa403a672',
 'Legacy.Maliev.Web.Tests/ServiceHeroResourceHintsParityTests.cs': '100644 blob fc54ab968611b5b41b5dd42481ebe979e9c94c53',
 'Legacy.Maliev.Web.Tests/ServiceOriginalImageRenderedParityTests.cs': '100644 blob c8986cfd7b41b5610440e1ca7189c7517db30846',
 'Legacy.Maliev.Web.Tests/ServicePageResponsiveSourceTests.cs': '100644 blob 4ed0a1dfad01feba5d0981bda0da84582a357a95',
 'Legacy.Maliev.Web.Tests/ServicePresentationCssParityTests.cs': '100644 blob 6e050b4fd8ba30d1577343cb0782522a3f1985d7',
 'Legacy.Maliev.Web.Tests/ServiceResponsiveImageBrowserParityTests.cs': '100644 blob edf9f8111ba5d16d4e7ccd44a2f447a573c270b1',
 'Legacy.Maliev.Web.Tests/ServicesDirectoryFinderContractTests.cs': '100644 blob 3706e08474132c24521b02ff8044e813bd9785df',
 'Legacy.Maliev.Web.Tests/ServicesStaticSsrRouteTests.cs': '100644 blob 8abe8bd5340de95a8a1cf89bb75a32eb05611ed1',
 'Legacy.Maliev.Web.Tests/SharedDiagnosticsContractTests.cs': '100644 blob e608262b661640ae94b44d6bc1e77d3c42017209',
 'Legacy.Maliev.Web.Tests/SharedFooterMigrationTests.cs': '100644 blob e33c15f7b9000afd1770758fa4a1055d9e4af307',
 'Legacy.Maliev.Web.Tests/SharedFrontendBundleContractTests.cs': '100644 blob 9b910a4b370ef4e66b65da7a60b89346cb0643c8',
 'Legacy.Maliev.Web.Tests/SharedNavigationMigrationTests.cs': '100644 blob 75c31fad541afb484e59a83f6157bcf46e8feee7',
 'Legacy.Maliev.Web.Tests/SharedSelectorValidationOriginalParityTests.cs': '100644 blob ff298acb5ebd9266e64edffa5ffb9b0886481e7b',
 'Legacy.Maliev.Web.Tests/SharedUiSourceParityTests.cs': '100644 blob 64d22cb4e125e1e521ebd4578456213ff18f8ecb',
 'Legacy.Maliev.Web.Tests/SignupPageTests.cs': '100644 blob 1d1933cacb91dee2172c880038381d7a8563bc7c',
 'Legacy.Maliev.Web.Tests/SignupStaticSsrRouteTests.cs': '100644 blob 64c9cafedde65d48f43baa6f57e255e8f26f0298',
 'Legacy.Maliev.Web.Tests/SiliconeCastingParityTests.cs': '100644 blob 3514a17ac9ea05bb03765a7bace7e21c2c4aea12',
 'Legacy.Maliev.Web.Tests/SitemapResponseCachingHttpTests.cs': '100644 blob 1707115bddcc56fb141d7fed0a53127306d9d388',
 'Legacy.Maliev.Web.Tests/SocialMediaStaticSsrRouteTests.cs': '100644 blob edd87bfcb8e4f9156eff0c8a82d85c266e291431',
 'Legacy.Maliev.Web.Tests/SourceAssetParityTests.cs': '100644 blob f085bf999a97ac5389ecee3cc73933c3858d618e',
 'Legacy.Maliev.Web.Tests/SourceCommitRegisterContractTests.cs': '100644 blob 64217d3c4a8e7c5e9386b9cb142b34fd01abddad',
 'Legacy.Maliev.Web.Tests/SourceParityThrough4486f0eTests.cs': '100644 blob 1c85df221a58a1904e64171e0cf5b1e032103791',
 'Legacy.Maliev.Web.Tests/SourceParityThrough7c416ccTests.cs': '100644 blob 0e6c6f617d6de71e1396cf4b26c9128422f16b25',
 'Legacy.Maliev.Web.Tests/SpaceMouseDeliveryContractTests.cs': '100644 blob 977e794f397957516ef716b44bccdb779ae64531',
 'Legacy.Maliev.Web.Tests/SpaceMouseOptionalBrowserTests.cs': '100644 blob 133f1f20dc73086cfd38897f00ab0f0424a7f271',
 'Legacy.Maliev.Web.Tests/SpecificationsIndexStaticSsrRouteTests.cs': '100644 blob 2b89d6d09609d485212635eadc9b034b160adab7',
 'Legacy.Maliev.Web.Tests/StartupDataProtectionMigrationTests.cs': '100644 blob 67797dff522b740097676a10e112d1b667becb5b',
 'Legacy.Maliev.Web.Tests/SyntheticPhysicalPricingTestData.cs': '100644 blob b7603a51b8926a82b0b24e5c1073d70a3ec00f02',
 'Legacy.Maliev.Web.Tests/TechnicalFilamentMinimumPricingTests.cs': '100644 blob d6f4cc134bddaf5778191a1d8461377bb5d05c76',
 'Legacy.Maliev.Web.Tests/TechnicalSeoParityTests.cs': '100644 blob c437c28ecf2416a10af862e063faed431cf06bd7',
 'Legacy.Maliev.Web.Tests/TermsConditionsStaticSsrRouteTests.cs': '100644 blob f835e6b5f678e8531bce2288ef281e4b2d4bdd10',
 'Legacy.Maliev.Web.Tests/TestAssemblyConfiguration.cs': '100644 blob 217120083bfa1b5b5a6400bb585b33f3651510f9',
 'Legacy.Maliev.Web.Tests/TestAssets/AdditiveBenchmark/filament-profile-catalog.v1.json': '100644 blob 75d52117137170a4e78fc38935635d3f0e5cb9e7',
 'Legacy.Maliev.Web.Tests/TestAssets/AdditiveBenchmark/filament-profile-catalog.v1.schema.json': '100644 blob '
                                                                                                 '544d5dcd5f575ad15e742a31e9659930d82fb003',
 'Legacy.Maliev.Web.Tests/TestAssets/AdditiveBenchmark/manifest.v1.json': '100644 blob 0a0fb526116a0a0adf4fd903b499df6ef8b5b08c',
 'Legacy.Maliev.Web.Tests/TestAssets/AdditiveBenchmark/manifest.v1.schema.json': '100644 blob 8e8df1bb3afcf4e9e3386c8725ad1fc699b6901f',
 'Legacy.Maliev.Web.Tests/TestAssets/AdditiveBenchmark/report.v1.json': '100644 blob 1420e7002ee356bcbe4a2d53a62e2fba3628f699',
 'Legacy.Maliev.Web.Tests/TestAssets/AdditiveBenchmark/report.v1.schema.json': '100644 blob 6e5a72ac82c6ae17f73980c2eda2884229818c2b',
 'Legacy.Maliev.Web.Tests/TestAssets/Cnc/axial-threaded-part.step': '100644 blob a48ba78cae59157c6e2afae3c0ee9b7e9b774f94',
 'Legacy.Maliev.Web.Tests/TestAssets/Cnc/box-20x30x40.step': '100644 blob 52a30283a7b073f69647643ed90d68170af1f2dc',
 'Legacy.Maliev.Web.Tests/TestAssets/Cnc/counterbore-pocket-bracket.step': '100644 blob 6a80a83c1f4d404ddfa8dc5e9e28fb8f3c103fd4',
 'Legacy.Maliev.Web.Tests/TestAssets/Cnc/flat-plate-100x100x4.step': '100644 blob a112599a32c88893aaf1cd7180b8519f6989f7ca',
 'Legacy.Maliev.Web.Tests/TestAssets/Cnc/handle-counterbore-recesses.step': '100644 blob 001bb9c31b15b96ffe0d7bc0a956eb3d8c7d8e31',
 'Legacy.Maliev.Web.Tests/TestAssets/Cnc/high-confidence-block-10x30x40.step': '100644 blob bb6aa1fef8375be86fb7356e68c39cd0c77a2d66',
 'Legacy.Maliev.Web.Tests/TestAssets/Cnc/mirrored-counterbore-pocket-bracket.step': '100644 blob 62c0c2151ccc9427261397721a1dfe97641efcbe',
 'Legacy.Maliev.Web.Tests/TestAssets/Cnc/round-disc-23x23x5.step': '100644 blob 8607f4e4e30f291657038437300a00a43cb33373',
 'Legacy.Maliev.Web.Tests/TestAssets/Cnc/threaded-cover-plate.step': '100644 blob 070d2aaed372691d6fd3bef09e86a14cba416186',
 'Legacy.Maliev.Web.Tests/TestAssets/Cnc/top-flip-bracket.step': '100644 blob adc761b16a86cf139a9939ba734132184a8cfbbf',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/.gitattributes': '100644 blob 55f8e05c3d6e04e31eb46477f8042b4e99903e32',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/AffinePcurveBand/analytic-compound-thread-small-bore-reversed.step': '100644 blob '
                                                                                                                                  '151ac4a60305b1993018097ea537881804488328',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/AffinePcurveBand/generate-analytic-compound-fixtures.cpp': '100644 blob '
                                                                                                                        '9120083499a9956062d92df50bcdf3b8ade7a966',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/AffinePcurveBand/original-analytic-compound-fixtures.json': '100644 blob '
                                                                                                                         '904b6d4cd21e277e58d52b50b54e4242b4f7ea0d',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/FiniteCurvedPatch/initial/generate-finite-curved-fixtures.cpp': '100644 blob '
                                                                                                                             '5b343d81db2f446e9a07f61b44b84ff176c1f02f',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/FiniteCurvedPatch/singular/generate-finite-curved-fixtures.cpp': '100644 blob '
                                                                                                                              '5b30f97effbed09b3bb9b21d14cb34eea0505857',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/FiniteCurvedPatch/thread/generate-finite-curved-fixtures.cpp': '100644 blob '
                                                                                                                            '7087cefee574815eee0a56af2608b86642e71d77',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/affine-pcurve-band-fixtures.json': '100644 blob '
                                                                                                '032813f60c28a0dae91f735d7ab39955d60e8de3',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-closed-channel-placed.step': '100644 blob '
                                                                                                   'dc00d88aedb2b884cf0a6d61ea751257a0cafe47',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-closed-channel-reversed.step': '100644 blob '
                                                                                                     '55bae3a1167a54c753c4f38ca6813c8e5c699c56',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-closed-channel.step': '100644 blob 2563920cb59a283856b20219be34f3c5c8fd1e9c',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-compound-fixtures.json': '100644 blob 3ba8c7bdbe96ba31f070c20f07d4e8fab38880ff',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-compound-groove-bead-placed.step': '100644 blob '
                                                                                                         'acbde3168cc93695d80f0b14ca3c0e91ffcb3951',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-compound-groove-bead-reversed.step': '100644 blob '
                                                                                                           '3a29b8fd49eb1ae998d9ced44b7aa9b7f9c2e372',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-compound-groove-bead.step': '100644 blob '
                                                                                                  'a14f9313dcbd084f0bb4e09b4632385b60bd2f84',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-compound-opposed-bores-placed.step': '100644 blob '
                                                                                                           '5b281787c6ebc243b438ec02c527854eed5b5de6',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-compound-opposed-bores-reversed.step': '100644 blob '
                                                                                                             '42b8d41b51ace5c56b269b0f4be57a23eda3ce45',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-compound-opposed-bores.step': '100644 blob '
                                                                                                    'eb9c6d3e4b42e0f05dc6871477f1f454e5a81066',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-compound-thread-small-bore-placed.step': '100644 blob '
                                                                                                               '6af08db2725366f93becac07539519e018c9e6e2',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-compound-thread-small-bore-reversed.step': '100644 blob '
                                                                                                                 '086922bac41931ba8837ed3e756c7d7e0397361d',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-compound-thread-small-bore.step': '100644 blob '
                                                                                                        'dec6dbe4439e380035af9b87a5d33b0e472db697',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-countersink-placed.step': '100644 blob '
                                                                                                '671d430c5cbaacab461514243f4379120176ff35',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-countersink-reversed.step': '100644 blob '
                                                                                                  '33d43c27d53c9f373ab63fd66efe4946e85cbf26',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-countersink.step': '100644 blob 56a1fa2aee554b7aee912d55187ce3210c1d74e1',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-drill-point-placed.step': '100644 blob '
                                                                                                'c04764b260c403a676d406012cdc850854c5174a',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-drill-point-reversed.step': '100644 blob '
                                                                                                  '9780f18e7be2c5a9e2b788afbd7250d4c9ee6e52',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-drill-point.step': '100644 blob f7573d47754ed237d326e55af07ff0bf840c6354',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-outside-chamfer-placed.step': '100644 blob '
                                                                                                    'dc89bba330f4318276d00b58e8cc1173cea808f4',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-outside-chamfer-reversed.step': '100644 blob '
                                                                                                      'ea9d528d0e20df3a2c7163a77afa7ca09b36ca8c',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-outside-chamfer.step': '100644 blob 57824bc907d9a2ecb357ed41e5f656652b0f68c9',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-countersink-placed.step': '100644 blob '
                                                                                                          'ea3627526fb024435fcf719a82a730bc64bb581f',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-countersink-reversed.step': '100644 blob '
                                                                                                            '372ec192658dd16b21d8a509a7034906ef2896b3',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-countersink.step': '100644 blob '
                                                                                                   '891793aa595099e331c44e36ed166b743e7ac961',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-drill-point-placed.step': '100644 blob '
                                                                                                          '7475029e4315928efb7bbf1511983eabdbb4d221',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-drill-point-reversed.step': '100644 blob '
                                                                                                            '4221165208a5fe76ab03b497c481147bba05214a',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-drill-point.step': '100644 blob '
                                                                                                   '6b3e7cfe1d8df8cb0b0a8871739036c93e5e3676',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-fixtures.json': '100644 blob '
                                                                                                '158a908adabc4a954439ee4c183ff7b149013243',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-open-bore-placed.step': '100644 blob '
                                                                                                        '4630bd634208d1c299a118bd4a8d2aa441e6c3eb',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-open-bore-reversed.step': '100644 blob '
                                                                                                          'a3ce801c7dc15688b4d87577729fd935a09d9dac',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-open-bore.step': '100644 blob '
                                                                                                 '0c8c849c7aa33a101234e87952219982cbd5e4cc',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-outside-chamfer-placed.step': '100644 blob '
                                                                                                              '95cadc45091c51f43e967918634f21490dad62ae',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-outside-chamfer-reversed.step': '100644 blob '
                                                                                                                '68ac8e83ca5feebf701d23121279fb6cf63ce9d9',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-outside-chamfer.step': '100644 blob '
                                                                                                       '271f5c1be4577bf3370a0ecf561a6edf1665aaa2',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-rotational-groove-placed.step': '100644 blob '
                                                                                                                'c89458e3328f2978063dd5583499b85c3f50bde9',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-rotational-groove-reversed.step': '100644 blob '
                                                                                                                  '8261c5b82edb88a0b555a74751ec9acf0bdbed59',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-rotational-groove.step': '100644 blob '
                                                                                                         '950871bcf8781fdbad60baa2e8f232c79684a671',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-tapered-through-placed.step': '100644 blob '
                                                                                                              '467809cf66cfcc28694e862923534d0f15846a91',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-tapered-through-reversed.step': '100644 blob '
                                                                                                                'c767d066da0057621af50a905f628665bc051b7e',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-primitive-tapered-through.step': '100644 blob '
                                                                                                       '357834642bbc973bc7021e9b2a55785988115a41',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-role-fixtures.json': '100644 blob 1766e57702739c53f2450a65b19c3427b063a71b',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-rotational-groove-placed.step': '100644 blob '
                                                                                                      'd0b284330802f5474b65910a9977138b8647e946',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-rotational-groove-reversed.step': '100644 blob '
                                                                                                        '08046dbe188565467e04abd2ce56aec9ec822325',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-rotational-groove.step': '100644 blob 1f001833477bed0c453319b1b859bd96326a114d',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-tapered-through-placed.step': '100644 blob '
                                                                                                    'd4ef1c2dacba2bd9f472fc9e4f9bd59ac0198c68',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-tapered-through-reversed.step': '100644 blob '
                                                                                                      '7c610fb36cde8c3ef50b2d4319819858b8b13d3c',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/analytic-tapered-through.step': '100644 blob da5ca3c413ad3b15c3df8de2f554c769bd3e56be',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/disk-cylinder.step': '100644 blob d53823ec0186b817b1461497dda5d822c77abc90',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/ellipse-cap.step': '100644 blob a2df81fce70066df4e7ae6b2e523936dee309486',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/ellipse-prism.step': '100644 blob 3ae76fd070d2235b59b96fe870c409e57e8ff2b5',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-fixtures.json': '100644 blob e37cdcdd69b52778b5c5e1d789aa321f643874c2',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-rational-roof-placed.step': '100644 blob '
                                                                                                       'be76ceb2b08bd8f67b52a14d8b0d57f665eed624',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-rational-roof-reversed.step': '100644 blob '
                                                                                                         '6a54c2534f98f65c17163ce3663f6e14e835f0cd',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-rational-roof.step': '100644 blob '
                                                                                                'a72cdf08383798a73284f4c5d899ef5efbf064aa',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-singular-roof-placed.step': '100644 blob '
                                                                                                       'd4d2e85c2404abd4d9a9628005fe676973844a1d',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-singular-roof-reversed.step': '100644 blob '
                                                                                                         '0f1f712a15d8387df654bc6ae5ebf0990d9daee7',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-singular-roof.step': '100644 blob '
                                                                                                '586eb8ac8082ff4fc0bd5f32e3491cb316dd8391',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-sphere-inward-placed.step': '100644 blob '
                                                                                                       '75c6a15059d0cb4b44ad0cb380c3e04672369b32',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-sphere-inward-reversed.step': '100644 blob '
                                                                                                         '554ce5dc210f262111de863b9e33b6b68c9caa04',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-sphere-inward.step': '100644 blob '
                                                                                                '86c401b957fabacc8679d13d4e33e89071d84ff7',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-sphere-outward-placed.step': '100644 blob '
                                                                                                        '6f8a51bea9a2976ad65cda23452cde93741995b9',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-sphere-outward-reversed.step': '100644 blob '
                                                                                                          'c301f1a01f55c3ce6686ddb0d7777dbe135007d1',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-sphere-outward.step': '100644 blob '
                                                                                                 '24870757814f683459b25542aa6901f9c96d8cce',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-thread-quarter-sphere-placed.step': '100644 blob '
                                                                                                               '80531b952b4f92cd8cc103d57d004b73637962d6',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-thread-quarter-sphere-reversed.step': '100644 blob '
                                                                                                                 '980f5eb5a3f0581a565c4bd8309fcc31c442b8dc',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-thread-quarter-sphere.step': '100644 blob '
                                                                                                        'aac2c17c716a831dc9b7c82ed5f8bcad0387a374',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-thread-small-sphere-placed.step': '100644 blob '
                                                                                                             'b6a889f46c95021e6841916dd313cd1c8fc2039e',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-thread-small-sphere-reversed.step': '100644 blob '
                                                                                                               '49f95a27976df03347625306e0e27ba79b13df90',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/finite-curved-thread-small-sphere.step': '100644 blob '
                                                                                                      '25273712b342fa19d895c92872240323938af27d',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/generate-analytic-compound-fixtures.cpp': '100644 blob '
                                                                                                       'e9537ab680175e2afa69532313ba062f5aad3889',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/generate-analytic-primitive-fixtures.cpp': '100644 blob '
                                                                                                        '3a5afedefdb3adc47dd78e8a3f1a730e432bfc26',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/generate-analytic-role-fixtures.cpp': '100644 blob '
                                                                                                   '6d8599fcfcc30d9cf2c6891f4599cf9b6aab0165',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/generate-finite-curved-fixtures.cpp': '100644 blob '
                                                                                                   '6096ae17df198257c83dcbbebfac1a4d3d4d03d1',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/generate-native-common-route-fixtures.cpp': '100644 blob '
                                                                                                         '600f7b142521f9a154137d12fd0d1a52cedf5c6e',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/generate-native-through-bore-negative-fixtures.cpp': '100644 blob '
                                                                                                                  '7281a130cba1aa039d6c9d17e19578bf1aefa62c',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/generate-region-fixtures.cpp': '100644 blob dd366402998f09e8a2bdfa597be62716445e856b',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/generate-stock-validation-fixtures.cpp': '100644 blob '
                                                                                                      '821b3865eeeaa918d3a9d42e736e87a557524754',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/generate-thread-fixtures.cpp': '100644 blob 3d0fc1e6f5c43ab9ac0fb1c316c83ebb47e36465',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/native-common-route-cavity-placed.step': '100644 blob '
                                                                                                      '3637f173d388c72550188166bb403500af232c28',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/native-common-route-cavity.step': '100644 blob daeb655829c844140e95801456392afb48c8408a',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/native-common-route-cuboid-placed.step': '100644 blob '
                                                                                                      '310baeaa5c8c09ff5d29e142d2f7bf49c72e4987',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/native-common-route-cuboid-reversed.step': '100644 blob '
                                                                                                        '1ec14714b260a7d2657f2c4b30ded546f1ee9095',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/native-common-route-cuboid.step': '100644 blob 5b20f8a7b2dece450b96f4bbd502644806291fc8',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/native-common-route-fixtures.json': '100644 blob '
                                                                                                 '73fbd7245deedb83a8a84c2039a45e1384a2abf8',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/native-consumer-fixtures.json': '100644 blob d7d1847456a6ad7f15c2414839519bef49f43626',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/native-through-bore-negative-fixtures.json': '100644 blob '
                                                                                                          'a66bb392679f64f5ad5f583c160911a95ea85e43',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/native-through-bore-negative-two-bores-placed.step': '100644 blob '
                                                                                                                  '3ab92a0ef912bda1371df62d13fefbb9a2110a06',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/native-through-bore-negative-two-bores.step': '100644 blob '
                                                                                                           '109b59853f36762dcc96244c9cd3c6a465793a3c',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/opposite-blind.step': '100644 blob 6a0f2c0aa49d6f0875b096c51f919b954aaa5573',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/partial-cylinder.step': '100644 blob 5ee9a8ac1ec58f2c5ae83dfc00555ab77c99a284',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/region-fixtures.json': '100644 blob f4340b8fcba18d3dc2f5c0a61e515c598d79277f',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/region-fixtures.md': '100644 blob 1bd511c2f64e79ba7e139868848b34ee19f53252',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/ring-default-precision.step': '100644 blob 26c0730bafe044e6f81f2ae16f3a0c016f6e876f',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/ring-flipped-axis.step': '100644 blob adfb5a16a2264a8a5a6a6e4bf9b85c24e6d6bb41',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/ring-split-edges.step': '100644 blob 6a43e02afdfefcb049bc513e3f6f225b9141542b',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/ring-transformed.step': '100644 blob 90308e9e20eb006e4fa74141ef08c2783f98a8e6',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/ring.step': '100644 blob d96fb1cc25ecd9b7eb3c72a24e33c3ca09d3f1e9',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/stepped-bore.step': '100644 blob ad7a9f5abd974a8a1bbec3e9fc7a18d2d7ade430',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/stock-validation-box-placed.step': '100644 blob '
                                                                                                '40a35411f83090f227026f55f507f7d339059a59',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/stock-validation-box-reversed.step': '100644 blob '
                                                                                                  '4890b866897d83daa691bfa77bdd22069533fb53',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/stock-validation-box.step': '100644 blob f6d93f2bf53bf7d009c1213531230296b892f5dc',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/stock-validation-coincident-instances-placed.step': '100644 blob '
                                                                                                                 '768de4c10148bae0e175b6cfc25566441d91ff34',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/stock-validation-coincident-instances.step': '100644 blob '
                                                                                                          'f3e4112cd8775b4ac2b772eb1bdd9dce76fbf09b',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/stock-validation-cylinder-placed.step': '100644 blob '
                                                                                                     'a97f53a185503a728c48d4477f7cded94a943116',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/stock-validation-cylinder-reversed.step': '100644 blob '
                                                                                                       'd5c490c81a4d58f63b11a216e7013231ab6c091e',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/stock-validation-cylinder.step': '100644 blob b176dac06899b95ce2c83b819b2bc4e806f00a50',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/stock-validation-fixtures.json': '100644 blob 10a15dfef4ba1ef9b5d93f28901fd85639629ed1',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/thread-external-lh-placed-reversed.step': '100644 blob '
                                                                                                       '91f5fe4fdc7db1b51431236093c64c3ded531147',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/thread-external-lh.step': '100644 blob dcf13098652b962c5dc1b7ae2705a343783120c5',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/thread-external-rh-placed-reversed.step': '100644 blob '
                                                                                                       'a44a4f9060b6927e0ef441d50811e00b56d0e12b',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/thread-external-rh-placed.step': '100644 blob a526f51d14070c79195c430c229655223357c26b',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/thread-external-rh-reversed.step': '100644 blob '
                                                                                                '1cf9c45cdd465d54c0723c5f893d0152d95b2348',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/thread-external-rh.step': '100644 blob 660ad8f1e7d11ca03a4dad50f05a12b762a8fed6',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/thread-fixtures.json': '100644 blob ba196de797a395f9b15ef6fb80ad6547c114e173',
 'Legacy.Maliev.Web.Tests/TestAssets/CncNativeInterpretation/thread-nonhelical-connector.step': '100644 blob '
                                                                                                '3285239f5bab6a0bb9cc63a4a5cc0a71e703ce31',
 'Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/default-morph-skin.gltf': '100644 blob 3c28f2c9c9d2f1b653c0f5b83f033d79b907f531',
 'Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/default-morph.gltf': '100644 blob 94287f979e3d4198854cb87355de0e700c6e9ebf',
 'Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/default-skin.gltf': '100644 blob 2605ee25745b537cc84c386b6e62677a5dafa46a',
 'Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/skin-influences-float-zero.gltf': '100644 blob fcb0db76b020e25dbf8f6677956a8bd66134e354',
 'Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/skin-influences-float.gltf': '100644 blob 9589c6bad810a16be33989d8e3f52b5966c8b8e5',
 'Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/skin-influences-uint16-sparse.gltf': '100644 blob 3f6f38355af23fac2ad36fbff59f9c9665158741',
 'Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/skin-influences-uint16-zero.gltf': '100644 blob 1576229754de99c16e9e9ceed6f39aabe35a2ff5',
 'Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/skin-influences-uint16.gltf': '100644 blob 49bc06f24391e4ca9f7daca35f73966a3a70d8cb',
 'Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/skin-influences-uint8-interleaved.gltf': '100644 blob 19c9df5ae65cadac44b576df953a4bdd719e2667',
 'Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/skin-influences-uint8-zero.gltf': '100644 blob 9a12be399b1b87202d6005fffc901022696cc709',
 'Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/skin-influences-uint8.gltf': '100644 blob d4b7f9bd886350889d7338f07c5dd0923009e7ec',
 'Legacy.Maliev.Web.Tests/TestAssets/cube.iges': '100644 blob 04b052d3bbf97c8d25b4f73b6668a270e16c87a4',
 'Legacy.Maliev.Web.Tests/TestResponseFailureEvidence.cs': '100644 blob 14712ddb1525487603460e775efa05217b590c92',
 'Legacy.Maliev.Web.Tests/TestResponseFailureEvidenceTests.cs': '100644 blob 92b6060963cbfb4ce16807acb7e4806efc7b713e',
 'Legacy.Maliev.Web.Tests/TestingRateLimiterLifetimeTests.cs': '100644 blob 5271a1d5f378db1627fa236d9302cefe1bdf41af',
 'Legacy.Maliev.Web.Tests/TestingWebApplicationFactory.cs': '100644 blob fb14855fe514d4e0ed79f892239c150876265b6b',
 'Legacy.Maliev.Web.Tests/ThaiLocalizationQualityTests.cs': '100644 blob 623d02cd8ce6511126c027eba436b2ff5c5540ef',
 'Legacy.Maliev.Web.Tests/ThaiLookupBillingPersistenceTests.cs': '100644 blob 20cf6977fcafa36bc4e2026d727e093abf81ed29',
 'Legacy.Maliev.Web.Tests/ThaiLookupBoundaryTests.cs': '100644 blob d57565be432f5092ef16f15da06acee1529fcb4c',
 'Legacy.Maliev.Web.Tests/ThaiLookupBrowserTests.cs': '100644 blob 7cd415db15f26f0e85b8eed93642baa208118c8f',
 'Legacy.Maliev.Web.Tests/ThaiLookupClientTests.cs': '100644 blob 3e5aeeb8f0639e7b8ac3546b807334acb75c203f',
 'Legacy.Maliev.Web.Tests/ThaiLookupRetryPolicyTests.cs': '100644 blob ade8d8391418f6eb98bb0cd156e3e8767502a5ed',
 'Legacy.Maliev.Web.Tests/ThaiLookupShippingPersistenceTests.cs': '100644 blob 58414d1a92d49423272c67a84b2c820c553a6c1d',
 'Legacy.Maliev.Web.Tests/ThaiLookupSurfaceHttpTests.cs': '100644 blob 9b037ddcdfd4090865c931ec60a4ca3d1cdd3c23',
 'Legacy.Maliev.Web.Tests/ThaiResourceParityTests.cs': '100644 blob 1cc2c42d19df23cea9e30db7524f110bc9a5a86b',
 'Legacy.Maliev.Web.Tests/ThreeDimensionalDesignParityTests.cs': '100644 blob 1fbbbe111aa2cd4ac8e8990668506a909cb8ced2',
 'Legacy.Maliev.Web.Tests/ThreeDimensionalPrintingCtrDecisionTests.cs': '100644 blob a1b7592df18552cd6cfa8ccb89f4afd73fa5498d',
 'Legacy.Maliev.Web.Tests/ThreeDimensionalPrintingGalleryBrowserTests.cs': '100644 blob 6c595d1afc9b171eb6de9ed04e995596f772d89f',
 'Legacy.Maliev.Web.Tests/ThreeDimensionalPrintingParityTests.cs': '100644 blob 8ad876abb394c6a2ce713c53970f66f25cabf986',
 'Legacy.Maliev.Web.Tests/ThreeDimensionalPrintingPartGalleryTests.cs': '100644 blob fd023bfe2eb41804fe7a66a3d637a7c8b5a68aac',
 'Legacy.Maliev.Web.Tests/ThreeDimensionalPrintingQuoteRouteBrowserTests.cs': '100644 blob 85a837ddb524c4a22c74b0a6129d7d3cc65818e5',
 'Legacy.Maliev.Web.Tests/ThreeDimensionalPrintingStaticSsrRouteTests.cs': '100644 blob ba383d64b1aa5f95b1214867138a03cdbfa22709',
 'Legacy.Maliev.Web.Tests/ThreeDimensionalScanningParityTests.cs': '100644 blob 5467c1cd7f013b91a8668be90df8fbd48f40b814',
 'Legacy.Maliev.Web.Tests/ThreeDimensionalScanningStaticSsrRouteTests.cs': '100644 blob fc917a8d371ef998f49b2e38407cc30ff008b7cc',
 'Legacy.Maliev.Web.Tests/UtilitySeoVerifierProofTests.cs': '100644 blob 082abe1d9658ccfaac37debe1d547eaa8eccbaa4',
 'Legacy.Maliev.Web.Tests/VoxelReconstructionTests.cs': '100644 blob 4b6e28c94cc0d819dd46875a86d3db2800780eb5',
 'Legacy.Maliev.Web.Tests/WallThicknessParityBrowserFixture.cs': '100644 blob 8db1b36a938752bbea2dde49095df461c89df204',
 'Legacy.Maliev.Web.Tests/WebApplicationFactoryLifecycleTests.cs': '100644 blob 5aee6dbe6fa94f9fda3a88f0cb1f626b400ebec1',
 'Legacy.Maliev.Web.Tests/WebContentSecurityPolicyMiddlewareTests.cs': '100644 blob 61b6adc7ddd5b7405c0f48fe05c402f9963dd43e',
 'Legacy.Maliev.Web.Tests/WebMcpSourceTests.cs': '100644 blob 2b3857c704281271344cd4dd839085dc8a660026',
 'Legacy.Maliev.Web.Tests/WebSurfaceLoginPhaseObservation.cs': '100644 blob 77a656586f201ba3f1e08b3c2db323db5ee060b9',
 'Legacy.Maliev.Web.Tests/WebSurfaceLoginPhaseObservationTests.cs': '100644 blob 6fc4df581cd5262ca6116e2871c1f3de66fa4c89',
 'Legacy.Maliev.Web.Tests/WebSurfaceOwnedLifetimeTests.cs': '100644 blob 42e4c8332629e6d25f1d5680a732d3abf0312a88',
 'Legacy.Maliev.Web.Tests/WebSurfaceTests.cs': '100644 blob 7c02dc458dedd8af5655f92a097b3851d967ffc6',
 'Legacy.Maliev.Web.Tests/WorkerAssetFingerprintTests.cs': '100644 blob 376d9ae7f3efb5001dfc6343e20c3e6c5c9581f6',
 'Legacy.Maliev.Web.Tests/WorkflowStaticSsrRouteTests.cs': '100644 blob f5968caf8c6f0d9af38b8d0fc61f5d4b0efb936e',
 'Legacy.Maliev.Web.slnx': '100644 blob 1befb7cdc809d1ebd9468a3c0e25ae1e7183ce5b',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/CNC/Cutting-Tools.cshtml': '100644 blob e0db0261d6c42982bf96fe02e80f7f0b331bce41',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/CNC/Feeds-and-Speeds.cshtml': '100644 blob 87dd8e4f1cff628e4bf2c8f20787c4dab457c212',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/CNC/Geometry-and-Workholding.cshtml': '100644 blob 91f9d4c8318f9ca3b04e337c49390e603671035c',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/CNC/Holes-and-Tolerances.cshtml': '100644 blob 7f92d00e7b0b95c8e1c11c06ba4c47821bb1fc27',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/CNC/Machining-Processes.cshtml': '100644 blob fe16b73930c4310f37fbdac449c4b5fb2f255e85',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Finishing/Heat-Treatment.cshtml': '100644 blob f99a9edc14050ffe701dc052ffd7f4ae717019fb',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Finishing/Surface-Finishes.cshtml': '100644 blob 7932c66dc62edeea17a907dc9237dda17ae3adf6',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Guidelines.cshtml': '100644 blob 7daaa3caa65aa288fca771a2dd9d31d6bb99bd23',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Guidelines.cshtml.cs': '100644 blob d600be53339f49914d7757ce9bafb9276d113dd5',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Index.cshtml': '100644 blob 23ffb22e955f6c3f42749891de9b35169b225f7a',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Index.cshtml.cs': '100644 blob 9a9224808d6e1145e811a0e4ac8c35511d24f7f1',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Inspection/Measuring-Parts.cshtml': '100644 blob 09579c8af49ac3b084dd8ac1e66e6a89fa748720',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Manufacturing/Process-Selection.cshtml': '100644 blob c88ced68b0955830bdcdb24be6df21bd6d46234c',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Materials/Engineering-Plastics.cshtml': '100644 blob 1105db994ec74f89e8d6e153240f4a9ccc36d666',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Materials/Metals.cshtml': '100644 blob 82535091e3391263b5f90aaab2665c40c7576a79',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Materials/Selection.cshtml': '100644 blob 4f971614355cc8d30bc518c23ecb6490f30da934',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Shared/_LayoutKnowledges.cshtml': '100644 blob 6f0ce2f6160ca1ca49d5bbd49afe9f5d5b7759b6',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Shared/_SidebarPartial.cshtml': '100644 blob b7c711cc0568ab2fe4e2781c83d8bf2bd4cc59c3',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Specifications/3D-Printing.cshtml': '100644 blob a71fd7cb6524da535e4a7cdbb762b71f772dd5ca',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Specifications/3D-Printing.cshtml.cs': '100644 blob 36d625c001bd0c7f11b7cc1f2b3a87f1b709377c',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Specifications/3D-Scanning.cshtml': '100644 blob ab49f31c0de0b1710141db8c246cf6987e141961',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Specifications/3D-Scanning.cshtml.cs': '100644 blob 8737ef6e999a8084c582e73147f15140b89cf5e5',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Specifications/CNC-Machining.cshtml': '100644 blob a2994eaa4715ebfd169e50fbf6af19e0c1d6464a',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Specifications/CNC-Machining.cshtml.cs': '100644 blob 072237f2e887302b877f56ab4891481c447b6a1d',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Specifications/Index.cshtml': '100644 blob 5b7ceef160c7cc70a3401e42da3db01b6667fe17',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Specifications/Index.cshtml.cs': '100644 blob a1414f30ff636d2ac3e7f2a77513aad6584237c2',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Workflow.cshtml': '100644 blob baf768be3ebaf64adf16dd5f3a7a04316df64629',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/Workflow.cshtml.cs': '100644 blob 7a6714ae9fd05b6e7bcd8c536f0558c9dda041bc',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/_ViewImports.cshtml': '100644 blob 9634d0f910057a8bbf72f95158bc1944e1b86767',
 'Legacy.Maliev.Web/Areas/Knowledges/Pages/_ViewStart.cshtml': '100644 blob b5a739059ebcb6caf36c777b85edceacb4c43093',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Index.cshtml': '100644 blob 3c94d61cb985649618660a46416748f12d0442d3',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Index.cshtml.cs': '100644 blob 522fbc48cdb40b24e7a9397db7b4d14026fc10d9',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Manage/Address.cshtml': '100644 blob 96524e19d10294d873179259fd80e5b42d360d46',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Manage/Address.cshtml.cs': '100644 blob f1667e630906dde0d3a0a92c2f15e9e79dcbdf66',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Manage/ChangeEmail.cshtml': '100644 blob b2b05bd6921441641d31558e6bd1d54d21399413',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Manage/ChangeEmail.cshtml.cs': '100644 blob 6ff29e573cc795f4eafbadfe3b52e333fa2b9942',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Manage/ChangePassword.cshtml': '100644 blob d70cd67f289c170ec79d6902939cddfec64963aa',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Manage/ChangePassword.cshtml.cs': '100644 blob 0fbc74a932fc8b2821cfd5c1ed7aa7a59a8034f6',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Manage/CreatePassword.cshtml': '100644 blob 83e348bc1b014e29ce6e27d67dc79a84f54c3627',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Manage/CreatePassword.cshtml.cs': '100644 blob 7d06e8c7b957a729c3ed37e5c9dde71ed1e0fb71',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Manage/Profile.cshtml': '100644 blob 0dbf9559ee90845691422782394b79a610578b97',
 'Legacy.Maliev.Web/Areas/Member/Pages/Account/Manage/Profile.cshtml.cs': '100644 blob 1b9994a5002c91b9f0e58ebb658e7cd9802e3aae',
 'Legacy.Maliev.Web/Areas/Member/Pages/Index.cshtml': '100644 blob baf76813be7b23e4ed60473b0f3d17cfd7a3166a',
 'Legacy.Maliev.Web/Areas/Member/Pages/Index.cshtml.cs': '100644 blob dd037293183e4d00a9c40131ea3d7c1672e81d23',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/3D-Printing.cshtml': '100644 blob 328c15abe8d89f2ef36a5d0f1bb84b842a730f84',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/3D-Printing.cshtml.cs': '100644 blob a926591adc01fce45b7b725b53d5b8fe174be4f0',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/3D-Scanning.cshtml': '100644 blob f8178b3a820191e6c7df3cc67ed8a463b8b94d2b',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/3D-Scanning.cshtml.cs': '100644 blob 14c497c469811fe5c409b3f711940cdc48cec626',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/CNC-Machining.cshtml': '100644 blob 37fdc66b3404a3f748ef5dbc4a4bf3e95d6ff35e',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/CNC-Machining.cshtml.cs': '100644 blob 342c681bc4371b64db26baf79efe877462629e72',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/History.cshtml': '100644 blob a2db9f9845b7e50c30368e595614ce3631712a94',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/History.cshtml.cs': '100644 blob 538b555bf00c15b861e4e11971eb0fa4352ba17d',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/Index.cshtml': '100644 blob 0755535ef89ce3a470e6c8c9b519dabc2410df4f',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/Index.cshtml.cs': '100644 blob 3fdda3cd88b22c9e212872b68dcaf0d8ab690a53',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/MemberOrderCreatePageModel.cs': '100644 blob 5ba489151c0696da152f7a4455d7f508bf63f99a',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/View.cshtml': '100644 blob 54c9aef3b2d70582ffdb0120824729b70f2da835',
 'Legacy.Maliev.Web/Areas/Member/Pages/Orders/View.cshtml.cs': '100644 blob 8ff817c88605d79b5ddbedf8c7932520a13ab491',
 'Legacy.Maliev.Web/Areas/Member/Pages/Quotations/Index.cshtml': '100644 blob d8d065fa08ec823a21bc50aa6e0eeead65e2cf00',
 'Legacy.Maliev.Web/Areas/Member/Pages/Quotations/Index.cshtml.cs': '100644 blob 16403c0891156c8fb0104ff16a7dce6505f50676',
 'Legacy.Maliev.Web/Areas/Member/Pages/Quotations/View.cshtml': '100644 blob 7d73aff5a11ae87747c7320ff4ff79baf49dd9b7',
 'Legacy.Maliev.Web/Areas/Member/Pages/Quotations/View.cshtml.cs': '100644 blob d051dc007d9352f5b38d175028c208102e87eabf',
 'Legacy.Maliev.Web/Areas/Member/Pages/Shared/_LayoutMember.cshtml': '100644 blob 8b6f7fa929ad60c1edefa3f5bb7d055ab6784196',
 'Legacy.Maliev.Web/Areas/Member/Pages/Shared/_MemberNavigation.cshtml': '100644 blob 4dd3cdaaa194329413b86bb1e3d2097a991e4524',
 'Legacy.Maliev.Web/Areas/Member/Pages/_ViewImports.cshtml': '100644 blob e0290136ef17ec4d30a9463d4228cabc1de75e3e',
 'Legacy.Maliev.Web/Areas/Member/Pages/_ViewStart.cshtml': '100644 blob 43d4139b0d37437272977a8ac175cc4f412d60d2',
 'Legacy.Maliev.Web/BuildIdentity.cs': '100644 blob 29a9ed02f4fd34caf54d934fef2f4b8db4d7a5ea',
 'Legacy.Maliev.Web/BuildIdentityEndpointRouteBuilderExtensions.cs': '100644 blob 3baeec8fc3dff869da827468eec2e511ed098cf8',
 'Legacy.Maliev.Web/CanonicalUrlPolicy.cs': '100644 blob 9e1b53cea570fdac13694c611494eaaccb810b8a',
 'Legacy.Maliev.Web/CncUploadAdmissionMiddleware.cs': '100644 blob 64079d3503fc454b864a2b54b139d4a55342b864',
 'Legacy.Maliev.Web/Components/Analytics/PublicContactChannelAnalytics.razor': '100644 blob a955e8fadba07ad3c7b434f90e8f0d506dc7ad49',
 'Legacy.Maliev.Web/Components/Analytics/PublicGoogleTagManagerBody.razor': '100644 blob 00b33aae4f0bf869d351b5799630ddc6f2167332',
 'Legacy.Maliev.Web/Components/Analytics/PublicGoogleTagManagerDisplayModel.cs': '100644 blob ed3bb0178e99839e015fb189276479e6a28c72c1',
 'Legacy.Maliev.Web/Components/Analytics/PublicGoogleTagManagerHead.razor': '100644 blob c4fbd3cfebc259da1d64d6870935f74adcc46433',
 'Legacy.Maliev.Web/Components/App.razor': '100644 blob cffc2d31c6a1d9fa6c543733b32fd4d0b76758e8',
 'Legacy.Maliev.Web/Components/Layout/PublicBrowserCompatibilityNotice.razor': '100644 blob 5bdf19075654430c075f889bfb5e41c0856ad7e9',
 'Legacy.Maliev.Web/Components/Layout/PublicBrowserCompatibilityNoticeDisplayModel.cs': '100644 blob bc3aa84e8d54653879f9263606f3e5822f656a0d',
 'Legacy.Maliev.Web/Components/Layout/PublicBusinessStructuredData.razor': '100644 blob 4a4b3cd3db9fcd04a4bd2d21ffd169433b20fda0',
 'Legacy.Maliev.Web/Components/Layout/PublicBusinessStructuredDataDisplayModel.cs': '100644 blob bdee916ece2f675edd17070a47edd3f6589af411',
 'Legacy.Maliev.Web/Components/Layout/PublicCookieConsent.razor': '100644 blob 59f71948f741dd7e77573f0631a6f41489542cab',
 'Legacy.Maliev.Web/Components/Layout/PublicCookieConsentDisplayModel.cs': '100644 blob a872d424a6820544319f2eea46442945c2d538a0',
 'Legacy.Maliev.Web/Components/Layout/PublicDocumentLinks.razor': '100644 blob fcb5f8b8735848d37bf3d80210f183387b6711a1',
 'Legacy.Maliev.Web/Components/Layout/PublicDocumentLinksDisplayModel.cs': '100644 blob d8f74789505529b7b396da76e3442b7eb3d96dbd',
 'Legacy.Maliev.Web/Components/Layout/PublicFooter.razor': '100644 blob b408b1e224bf53ca4a563a558e9e60c6b2865dd1',
 'Legacy.Maliev.Web/Components/Layout/PublicNavigation.razor': '100644 blob e17e57686aeda95fba7f3d7a2a47acaa36e76765',
 'Legacy.Maliev.Web/Components/Layout/PublicNavigationDisplayModel.cs': '100644 blob 962072404789d62ad8a3cfe3e866813a9ab6fbd5',
 'Legacy.Maliev.Web/Components/Layout/PublicOpenGraphMetadata.razor': '100644 blob a22824dd81e5e5eaaf009440ffd97d27fb2e1843',
 'Legacy.Maliev.Web/Components/Layout/PublicOpenGraphMetadataDisplayModel.cs': '100644 blob 826ce2e65733cf8c45bca9872a7421dd2d36c12f',
 'Legacy.Maliev.Web/Components/Layout/PublicRouteAssets.cs': '100644 blob bd4e23e5582d98e0ee94790dbd72fe8cd545f239',
 'Legacy.Maliev.Web/Components/Layout/PublicWebsiteStructuredData.razor': '100644 blob b5d4e84ce0dc8c10b5fa4b1ebe123417ed98fef4',
 'Legacy.Maliev.Web/Components/Layout/SocialLinks.razor': '100644 blob 790050581dcb5abc0fc8869fa80ffc3a68b566a3',
 'Legacy.Maliev.Web/Components/Lookups/ThaiLookup.razor': '100644 blob c74d297c68c3fcd9d91eb3ec6c18396a2d8d5434',
 'Legacy.Maliev.Web/Components/Lookups/ThaiLookupSurface.razor': '100644 blob 8b45f9f2c7dfdb3fa3e91411a0711daf7d69150d',
 'Legacy.Maliev.Web/Components/Metadata/PublicServiceStructuredData.razor': '100644 blob 2d6e587716d0796c0d094d8cea59144916843c95',
 'Legacy.Maliev.Web/Components/Metadata/PublicServiceStructuredDataDisplayModel.cs': '100644 blob 815bdaea880d4779c99ceea0a648d7cbef8f754e',
 'Legacy.Maliev.Web/Components/Pages/About/AboutContent.razor': '100644 blob b27b1e79b0de436fa8f88d1f94ecb9416a13d6b5',
 'Legacy.Maliev.Web/Components/Pages/About/AboutPage.razor': '100644 blob 64cd195dc222547e3b6b98123557699e97946638',
 'Legacy.Maliev.Web/Components/Pages/About/AboutWorkpieceGallery.razor': '100644 blob fc24dd81cdbc7ddac8514016f7c8db9b4db6d1a2',
 'Legacy.Maliev.Web/Components/Pages/About/CorporateCustomers.razor': '100644 blob 94ee18e59c7f6bec80b47d0711fd9d4469d11714',
 'Legacy.Maliev.Web/Components/Pages/About/SocialMediaContent.razor': '100644 blob 593bb22904b7f12e61b39cf59b4040554fe2b926',
 'Legacy.Maliev.Web/Components/Pages/About/SocialMediaPage.razor': '100644 blob 6aee795263fb872558f2455253989d87f030914f',
 'Legacy.Maliev.Web/Components/Pages/Account/AccessDeniedContent.razor': '100644 blob b5e15d48ed695767f680e996d2d00d45a286a845',
 'Legacy.Maliev.Web/Components/Pages/Account/AccessDeniedPage.razor': '100644 blob 3fa6bf232a04f57dab0517e746c1542389fd14fd',
 'Legacy.Maliev.Web/Components/Pages/Account/AccountIndexContent.razor': '100644 blob e5dd8346495de221c08f802f6aeca2dee50d49f4',
 'Legacy.Maliev.Web/Components/Pages/Account/AccountIndexDisplayModel.cs': '100644 blob 989f77428165bc801bb39680e2ea055518e7ccc4',
 'Legacy.Maliev.Web/Components/Pages/Account/AccountIndexPage.razor': '100644 blob 91e5dc21ef4ec6628d1cbd3ef0c80ab1804e3499',
 'Legacy.Maliev.Web/Components/Pages/Account/ChangeEmailConfirmationContent.razor': '100644 blob 1d9a4ae9eddf9307203d6633eb892cacf2c0612a',
 'Legacy.Maliev.Web/Components/Pages/Account/ChangeEmailConfirmationDisplayModel.cs': '100644 blob 78a4e72965f3ff925de9cf23784ba856e9db0537',
 'Legacy.Maliev.Web/Components/Pages/Account/ChangeEmailConfirmationPage.razor': '100644 blob 96c6081b442c204ff55c7ec539bef40315b15b6d',
 'Legacy.Maliev.Web/Components/Pages/Account/EmailConfirmationContent.razor': '100644 blob f30d990a5677aa8e51bd0c165ba3b26437ccd15d',
 'Legacy.Maliev.Web/Components/Pages/Account/EmailConfirmationDisplayModel.cs': '100644 blob e8e1ee1878eac94bcb41b71d36f0428f7fb48fab',
 'Legacy.Maliev.Web/Components/Pages/Account/EmailConfirmationPage.razor': '100644 blob a293dc48ad260dce35640f10962a32b17d69f741',
 'Legacy.Maliev.Web/Components/Pages/Account/ForgotPasswordContent.razor': '100644 blob 4c1ab133116a7bfd5f62fd5e0ca1e8572096695a',
 'Legacy.Maliev.Web/Components/Pages/Account/ForgotPasswordFormDisplayModel.cs': '100644 blob b3b06647de7ab0c89c0d770fda3ccbcc27b845ee',
 'Legacy.Maliev.Web/Components/Pages/Account/ForgotPasswordPage.razor': '100644 blob 4484338e4cc35f54473ae9e3c8a677d8802ac96d',
 'Legacy.Maliev.Web/Components/Pages/Account/LoginContent.razor': '100644 blob 8cc3f3487ac9c3a44a9672cef50f6e0becdcbcb5',
 'Legacy.Maliev.Web/Components/Pages/Account/LoginFormDisplayModel.cs': '100644 blob dcb23355d33ad266f430a0e4b8a40642b81ef655',
 'Legacy.Maliev.Web/Components/Pages/Account/LoginPage.razor': '100644 blob d52319dabd95ca2bfba5acf3513a4959edc69d99',
 'Legacy.Maliev.Web/Components/Pages/Account/LogoutContent.razor': '100644 blob 92b9405bc75f38c6896070a484a01ec4d9e2d313',
 'Legacy.Maliev.Web/Components/Pages/Account/LogoutPage.razor': '100644 blob f7e839242437792a1c82b7d2f1836439bf33dcf8',
 'Legacy.Maliev.Web/Components/Pages/Account/ResetPasswordContent.razor': '100644 blob dd64783fb538b7e983d3daabc1c0838f5f7255c4',
 'Legacy.Maliev.Web/Components/Pages/Account/ResetPasswordFormDisplayModel.cs': '100644 blob 2ae845e622be73de01b439fabefa64781b261db4',
 'Legacy.Maliev.Web/Components/Pages/Account/ResetPasswordPage.razor': '100644 blob b89af224f7a3aa1d558932a0e0f020f825eb45f3',
 'Legacy.Maliev.Web/Components/Pages/Account/SetInitialPasswordContent.razor': '100644 blob 4e568e088c75e3add20bf427eaa4164c0ec67f30',
 'Legacy.Maliev.Web/Components/Pages/Account/SetInitialPasswordFormDisplayModel.cs': '100644 blob e5441bdea1a49e939f4daef430d0dfe885981feb',
 'Legacy.Maliev.Web/Components/Pages/Account/SignupContent.razor': '100644 blob a281be6abf85c7b10cc4aff2feb23c10db197236',
 'Legacy.Maliev.Web/Components/Pages/Account/SignupFormDisplayModel.cs': '100644 blob ad9f588bff389db344f3f89ae0b8b54fa72b6b98',
 'Legacy.Maliev.Web/Components/Pages/Account/SignupPage.razor': '100644 blob 69fb111e814a39d4ce26e9cd9429ff55672104ea',
 'Legacy.Maliev.Web/Components/Pages/Career/CareerDetailContent.razor': '100644 blob 862ea51e4bb2fe848ab45a4383c172aa70cfed58',
 'Legacy.Maliev.Web/Components/Pages/Career/CareerDetailContentModel.cs': '100644 blob 3bbf8f93108357bfc9c65ba288c9eb7915a16117',
 'Legacy.Maliev.Web/Components/Pages/Career/CareerDetailPage.razor': '100644 blob 9b5be2b3683b4a0843f5c6c3b8d55e6ca3922df8',
 'Legacy.Maliev.Web/Components/Pages/Career/CareerIndexContent.razor': '100644 blob bca1323fc8af0786674dbcae90fad8471adf289d',
 'Legacy.Maliev.Web/Components/Pages/Career/CareerIndexContentModel.cs': '100644 blob a4174b83830ae3e8288e83222479c3fd4b9b16cf',
 'Legacy.Maliev.Web/Components/Pages/Career/CareerIndexPage.razor': '100644 blob f74b0ce08689a3df64cfafbeada339a5b7487e8f',
 'Legacy.Maliev.Web/Components/Pages/Career/CareerOfferPresentation.cs': '100644 blob 71cbcc96e7bfcdc901ca2eebdebd6c78ef2f641d',
 'Legacy.Maliev.Web/Components/Pages/Contact/ContactContent.cs': '100644 blob 4be656a5d14c35c42a30c1699ca534d32e8fd428',
 'Legacy.Maliev.Web/Components/Pages/Contact/ContactDetailsContent.razor': '100644 blob f7858995695ac27d86c8cd5d1b687fcf5392cf4e',
 'Legacy.Maliev.Web/Components/Pages/Contact/ContactFormDisplayModel.cs': '100644 blob 38860975af4632439cb5db7afc97ca386b54123a',
 'Legacy.Maliev.Web/Components/Pages/Contact/ContactFormFields.razor': '100644 blob da4142249dac2352a83457f43f5c485931f14d24',
 'Legacy.Maliev.Web/Components/Pages/Contact/ContactHeroContent.razor': '100644 blob d6ad937f06b8d8f2a4352d6c2816249f21926a98',
 'Legacy.Maliev.Web/Components/Pages/Contact/ContactMapDisplayModel.cs': '100644 blob 3f5247c5359dc34ee289c97f8df7eaa467770c01',
 'Legacy.Maliev.Web/Components/Pages/Contact/ContactPage.razor': '100644 blob e77a563752a50f48b5d3f2a8d4eec7665787c47b',
 'Legacy.Maliev.Web/Components/Pages/Contact/LineContactPage.razor': '100644 blob adff47390f8a7d971b8635795fd0a8dc94241bce',
 'Legacy.Maliev.Web/Components/Pages/ErrorContent.razor': '100644 blob 013d2054ce1904044eb834cdd2354c9bc0748792',
 'Legacy.Maliev.Web/Components/Pages/ErrorDisplayModel.cs': '100644 blob f1dc55c02ca73ede1eb4f108bb789dbb0550e1dc',
 'Legacy.Maliev.Web/Components/Pages/ErrorDisplayModelResolver.cs': '100644 blob eaca98e1ea0ca65372877cff8d4c1fb1442c4d52',
 'Legacy.Maliev.Web/Components/Pages/ErrorPage.razor': '100644 blob af0a00748fe6a0d2b2353ee84368ae75d1ffe1db',
 'Legacy.Maliev.Web/Components/Pages/Home/HomeContent.razor': '100644 blob a5245b967c9baad17c69d0370da33907a317aea6',
 'Legacy.Maliev.Web/Components/Pages/Home/HomeHero.razor': '100644 blob 53252f586b706ffb888f66fc2c74cdc1db5d5e0d',
 'Legacy.Maliev.Web/Components/Pages/Home/HomeHeroScene.razor': '100644 blob b3b3105c745d4eae746d6a11f69147374829f092',
 'Legacy.Maliev.Web/Components/Pages/Home/HomeHeroSelection.cs': '100644 blob 7f04b5688d3c42f9c014253ad250fa245581b8af',
 'Legacy.Maliev.Web/Components/Pages/Home/HomePage.razor': '100644 blob a1a38551f573bbe901b4c0c812c36566eba1b563',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncAuthenticatedProfile.cs': '100644 blob 0437aa6720f46468d3a1b3e6318f098662cd4ad9',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncAuthenticatedProfileLoader.cs': '100644 blob 4cb6ea59a43bd06024bf816c274d8d0fe445357f',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncFileAdmissionValidator.cs': '100644 blob 739c3be22b65465f2602b5c91ba53f845517b718',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncNotificationComposer.cs': '100644 blob 6dcf8a910aba86b35f5bdda9f11f8c88ab95abea',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncNotificationCoordinator.cs': '100644 blob 915f2679c1f94d3929a6546ba58f094cfae3e5ea',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncProtectedUploadBindings.cs': '100644 blob 44c2b364223b55ea1c88020df19b9bd2d15b695f',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncQuotationSession.cs': '100644 blob 73816188a843a30fd2f5f2f6a0457dbcc88ef361',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncReceiptClaimCoordinator.cs': '100644 blob 6c590a6272bfb39207a37c422be44ce1645814f8',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncSubmission.cs': '100644 blob bde5cb9b416cbfe5feb9b2e206d68909290f2fdb',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncSubmissionAdmission.Items.cs': '100644 blob 391e4f1b79715bb014dd6e2c31c8673f706723d4',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncSubmissionAdmission.Review.cs': '100644 blob 4154eef9174265bb5fbbb1ad63392383c654fe3e',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncSubmissionAdmission.Snapshot.cs': '100644 blob 84f6552c5f14a484883385fc167b0bd54bb38d41',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncSubmissionEndpoint.cs': '100644 blob 3644254617b3fc9ddc5f4b96e6ee25a74a5913de',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncSubmissionFormBinder.cs': '100644 blob 4b9ac8d1653b93f5fc558d511191dd67b8140b37',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncSubmissionPersistenceCoordinator.cs': '100644 blob 0d0976a2dc0482e9814d61e08c72472aa59970e2',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncUploadHandler.cs': '100644 blob 206fa4710005f8280781af3696985b8551ccd4b6',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/CncUploadReceiptStore.cs': '100644 blob e83baad8b03c73cc7c9790db17773ea1c219efb6',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationAnalytics.cs': '100644 blob a0cb6664566f81918c7478226fc54fda68bd290a',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationBulkSavings.cs': '100644 blob 5dbdf4a8f23e33c293e6eebf95e66c38b2ef763d',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationCalculator.cs': '100644 blob b69c37cf494da5deb8421b83361ce37df2bcee80',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationCircuitCultureCookie.cs': '100644 blob '
                                                                                                '3ca9a45e5ace2554ae882d3643e50633c5b3b272',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationCompatibilityEndpoint.cs': '100644 blob '
                                                                                                 '8b9c81c1ced7f5d686ea5722468433f9df4cae67',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationCountryLabels.cs': '100644 blob 6fbaa90735d16e5867a5afb2762b1fc42c4b7e03',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationCustomerDisplayModel.cs': '100644 blob '
                                                                                                '633427617334edf00e8b6e5e75ea5042e7dab04f',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationCustomerForm.razor': '100644 blob 9134c746b93647604620f1037fd8cbb23b0c196c',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationCustomerOrderOverview.razor': '100644 blob '
                                                                                                    'a63b3d662b0e2b148a1c0f5103a0800427990bba',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationDisplayModel.cs': '100644 blob 073a1b9276a8ae2aae33c5bf11ce825aa03b1d55',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationEmptyScene.razor': '100644 blob 7c39da5eefa9d6d4a84b371dd14be0e4d9509a81',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationFxHandler.cs': '100644 blob 90b2325ccd90ba90601ab90ec5a74a810dda59c7',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationMaterialPriceDisplaySnapshot.cs': '100644 blob '
                                                                                                        'eaa2d4021cf481bd809d14862198422e616c8d48',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationMaterialSelector.razor': '100644 blob a265a5dbb75c9e9b80295f4368de41b637061cdc',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationPage.razor': '100644 blob 51f2b94ed3bb325e8a424437283e194ab6ceabe0',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationPhysicalResult.razor': '100644 blob be46a10c4b312ec10917acff77037b9841931c14',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationPreliminaryQuotation.razor': '100644 blob '
                                                                                                   '667d8c732ad452366572657f9dfa5479dd959c91',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationReview.razor': '100644 blob f0bc551b02e14f05997d6a08ee84c6a190f2dfac',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationSelectedPrintTime.cs': '100644 blob 6eec956fabb4f13682957189eaee531635a4de46',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationWorkflow.razor': '100644 blob d13b5a554b0380ecec7fab2cfd3fa14d91c94df2',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationWorkflow.razor.cs': '100644 blob 93f3a4318247c3a5520fb375551090b7f6e8f284',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationWorkflowCoordinator.cs': '100644 blob 9a2830ffc95276b62295014b8befd2800eed4b07',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationWorkflowState.cs': '100644 blob 068443161d01e8fba5faecfb3d3dcb8a2392d1e9',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/JsInstantQuotationAnalyticsSink.cs': '100644 blob e4fc37e056a568df6e4f286b826b14c965ff2b1c',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/MaterialCardPresentationCatalog.cs': '100644 blob 8c5422dabf4527709d04d0ad6fba1fbdf40f0dcb',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/ProtectedCookieInstantQuotationWorkflowSessionIdentityAccessor.cs': '100644 blob '
                                                                                                                          '9cc3e0cc2dadbbafc75e8b784b701997f042456e',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/QuantityEditState.cs': '100644 blob c7d71dffeb1cc13b8b0e4d8062f2120ed5675533',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/RedisCncUploadReceiptStore.cs': '100644 blob 6917a82111e3400903f4e87ec9cd76554cafde10',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/RepricingActivity.cs': '100644 blob bd5d31ac4259fe6f1783cfe406c32d42bce70303',
 'Legacy.Maliev.Web/Components/Pages/InstantQuotation/ThreeDimensionalPrintingEstimateContent.razor': '100644 blob '
                                                                                                      '9fee29de00fcee90b47d7c5043b4c930c67834a2',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/CncCuttingToolsContent.razor': '100644 blob c6ee2d87b962d142c63a280a987e5a24e7de405e',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/CncCuttingToolsPage.razor': '100644 blob cfd5867ba5438a311bdcbf4a2f70f7cd0a5105bd',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/CncFeedsAndSpeedsContent.razor': '100644 blob 5e1792cecf000890e4838d63209ac90f546f1dcb',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/CncFeedsAndSpeedsPage.razor': '100644 blob fabc1de9e50bd633e0f8b31cd0dd5309bd19cf5d',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/CncGeometryAndWorkholdingContent.razor': '100644 blob 80449596541303bed152e7ac25b7ebbb35f780cd',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/CncGeometryAndWorkholdingPage.razor': '100644 blob b23e2fd4d61fde2ad36e0e2d00ed7e3eda7294f7',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/CncHolesAndTolerancesContent.razor': '100644 blob 95be24d285380265f2155fca3d138e5cfda02ec7',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/CncHolesAndTolerancesPage.razor': '100644 blob 7e4fc95d982872d75304302bbf977958b962e341',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/CncMachiningProcessesContent.razor': '100644 blob 5adf184cc61ec0675d18809b6b8d3683360752e8',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/CncMachiningProcessesPage.razor': '100644 blob 9a56c507ef1a366139492c267f1d92085fbb03ad',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/FinishingHeatTreatmentContent.razor': '100644 blob bc3433f0547acab4b9ec90abbee65491281d9241',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/FinishingHeatTreatmentPage.razor': '100644 blob 0b774eb8f61cb4aca1b8755d22afbbe08256134e',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/FinishingSurfaceFinishesContent.razor': '100644 blob 45aaaef8cd91284945d4f94ff6deabd480c8802e',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/FinishingSurfaceFinishesPage.razor': '100644 blob d3828781edb1c12e35cb6d16af0d2e535693150e',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/GuidelinesContent.razor': '100644 blob 32a45bb2d88377047275601362094e4f0a50bcd5',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/GuidelinesPage.razor': '100644 blob b2ddd7921c632dd3179dd1038c216aba94954ecd',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/InspectionMeasuringPartsContent.razor': '100644 blob 2f3cd84661c2d8741e6583c3f098c2a9ac696d6b',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/InspectionMeasuringPartsPage.razor': '100644 blob 351d52af89b8628c769497f3278b4ae8e50f7538',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/KnowledgeGuidelinesStructuredData.razor': '100644 blob ab2d0bd505c9fee9775488fab7c72db03e40b18d',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/KnowledgeIndexContent.razor': '100644 blob 36d9dd54c2e395a98498af16adf6f74315585e55',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/KnowledgeIndexPage.razor': '100644 blob f8b3b6c3509272928531059189555a4aba81e6dd',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/KnowledgeNavigation.razor': '100644 blob 34555cad6aef759b5b529c5d8b05ec3fc986e845',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/KnowledgeShell.razor': '100644 blob 0afdfa894b7ad0e5a73f14b3fcf2fa85b7d498da',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/ManufacturingProcessSelectionContent.razor': '100644 blob 261bdf497dabb76d7427efe81f7d3576d164252e',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/ManufacturingProcessSelectionPage.razor': '100644 blob e9caba38b5ac56e63d7c7c7265a15963772275db',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/MaterialsEngineeringPlasticsContent.razor': '100644 blob d61c033c097bf199caaec30ea600a66c9faf19a8',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/MaterialsEngineeringPlasticsPage.razor': '100644 blob 2f6f4dffd670fa5763d5fcca83753685f1d7c667',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/MaterialsMetalsContent.razor': '100644 blob a360ed31da8b1a8fd0336225925c1638d529642b',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/MaterialsMetalsPage.razor': '100644 blob 15f8dfd41313c58ee9ce98ff344c3fe55bf103bc',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/MaterialsSelectionContent.razor': '100644 blob 4cae3d7ab56044da538cbe9779ed1ad7f0cc97f0',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/MaterialsSelectionPage.razor': '100644 blob 31c2c275f6ecd37f81c5a4312318594f620cf941',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/Specifications/CncMachiningContent.razor': '100644 blob d434b029a3821fb8016e62949e514dc73cf8747d',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/Specifications/CncMachiningSpecificationPage.razor': '100644 blob '
                                                                                                     '4a2222ba15566efd046a5a383dfde87301d472cc',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/Specifications/SpecificationsIndexContent.razor': '100644 blob '
                                                                                                  '608afee8c36c2d3f4869c15aa52737d77a54246a',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/Specifications/SpecificationsIndexPage.razor': '100644 blob bc45d58b3c65dc1097908819ee4bcffc16da69ba',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/Specifications/ThreeDimensionalPrintingContent.razor': '100644 blob '
                                                                                                       'e0df19d8d7c0c24c08eba69ee65727c9724a0a10',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/Specifications/ThreeDimensionalPrintingSpecificationPage.razor': '100644 blob '
                                                                                                                 '7a315e89d3792cfda2860580eeaa0d437af4d073',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/Specifications/ThreeDimensionalScanningContent.razor': '100644 blob '
                                                                                                       'cd8cd1f2ad977ef70c4ba2de81ceda81e298d8d8',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/Specifications/ThreeDimensionalScanningSpecificationPage.razor': '100644 blob '
                                                                                                                 '9bc57990c3176e1c5a1035fd1b8104772a68dd7e',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/WorkflowContent.razor': '100644 blob 89f248be7d2bd7512bcc8459261bdbfeb0d7e315',
 'Legacy.Maliev.Web/Components/Pages/Knowledges/WorkflowPage.razor': '100644 blob 5ce3db4d3b23f07fe940ec61c68fffaee1dfbb38',
 'Legacy.Maliev.Web/Components/Pages/Legal/LegalContent.razor': '100644 blob 7e023a47ffce877b2d17cdc0124462df31fb7710',
 'Legacy.Maliev.Web/Components/Pages/Legal/LegalPage.razor': '100644 blob 616db73a482ea2f5a5ce54f5611ed844087b238a',
 'Legacy.Maliev.Web/Components/Pages/Legal/NoWeaponsContent.razor': '100644 blob c3eb3d3acc46c77d18a22755a6ec63eb445f9dcc',
 'Legacy.Maliev.Web/Components/Pages/Legal/NoWeaponsEnglishContent.razor': '100644 blob 6526fba4101b6acc8a2fbae66ad39a2a6df8d7ca',
 'Legacy.Maliev.Web/Components/Pages/Legal/NoWeaponsPage.razor': '100644 blob 5ccc8ea80e42f2f61daba521696c06fa85d62c8e',
 'Legacy.Maliev.Web/Components/Pages/Legal/NoWeaponsThaiContent.razor': '100644 blob a6f468aa34542025a7ec433da2a6d0f9713c65e4',
 'Legacy.Maliev.Web/Components/Pages/Legal/NonDisclosureAgreementContent.razor': '100644 blob 552e193a5b5dafcb3ba96052b570556a72a5bc39',
 'Legacy.Maliev.Web/Components/Pages/Legal/NonDisclosureAgreementPage.razor': '100644 blob f14acfb9d8e80cac40d08b0463b3fb9ed543c13c',
 'Legacy.Maliev.Web/Components/Pages/Legal/PrivacyPolicyContent.razor': '100644 blob 39b49ad179f36b216a805cdfdb7f4ea48e04ad89',
 'Legacy.Maliev.Web/Components/Pages/Legal/PrivacyPolicyPage.razor': '100644 blob b0f3416c8b89351b199fd27f14fc981921d2253d',
 'Legacy.Maliev.Web/Components/Pages/Legal/PrivacyPolicyThaiContent.razor': '100644 blob 5618310d4f07ec50043868e766006d9880f38d46',
 'Legacy.Maliev.Web/Components/Pages/Legal/TermsConditionsContent.razor': '100644 blob 0cb6a7b92b5492eca3c77a8c932e19a9d3174b1d',
 'Legacy.Maliev.Web/Components/Pages/Legal/TermsConditionsEnglishContent.razor': '100644 blob d7d7c90e42a8b24db4a4df6888ac76a04de41601',
 'Legacy.Maliev.Web/Components/Pages/Legal/TermsConditionsPage.razor': '100644 blob 69d6fb429f6b12888be554a0c970853d8635fbfc',
 'Legacy.Maliev.Web/Components/Pages/Legal/TermsConditionsThaiContent.razor': '100644 blob 38e9215bf5de508d365c6e525c6fef91f5420b6b',
 'Legacy.Maliev.Web/Components/Pages/Member/AddressSummary.razor': '100644 blob f1b3b3c056461033a7c97e32c71a8fd303380991',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberAccountIndexContent.razor': '100644 blob 9e78d103a63c07f31bb5ed83d6b8e3327a66f462',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberAccountIndexPage.razor': '100644 blob 0b1ff17b9a021a7c1ddf8efb221e25d122a0e86f',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberAccountLoaders.cs': '100644 blob 9ef0517e0d90d9ced205fa4c42c479a84ff231fb',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberAddressContent.razor': '100644 blob 588518933de884df215d0cb7319e597800a03c98',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberAddressDisplayModel.cs': '100644 blob 9587b39aad328bf930cf19886468a3214efa0f80',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberAddressPage.razor': '100644 blob 24125de93d935cae7ccde2b6d211f790aa1b6294',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberChangeEmailContent.razor': '100644 blob 46b1d1e7e78ddfe7ddd27cbfa985a421fb848ffc',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberChangeEmailDisplayModel.cs': '100644 blob 8520a8953d5563078603ed726e84ced8c53178ac',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberChangeEmailPage.razor': '100644 blob ba1dadc5a8188301157f1473a2ea6c105cd232b1',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberChangePasswordContent.razor': '100644 blob 0758f079c4001afb644eb615684efa3f0eaf5acb',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberChangePasswordDisplayModel.cs': '100644 blob 00675bd539cd428257c1107c7173174662771694',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberChangePasswordPage.razor': '100644 blob eb257f092b6874972925b3ed1a9a5002955c66d2',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberCreatePasswordContent.razor': '100644 blob 55c10f41e63f970338642f1e5498659e87e2d40d',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberCreatePasswordDisplayModel.cs': '100644 blob 9fe718bc1de15fe9913492fc334845a58f7b9d92',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberCreatePasswordPage.razor': '100644 blob 493c68421f1a9e471a7d246737d216ff3b5d9cfe',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberDetailLoaders.cs': '100644 blob 5e390c37a6ff29a49c7601d42a5f010470e04a03',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberDocumentsContent.razor': '100644 blob 84a253938ce1bd14db946fb4e4862337cd77949c',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberDocumentsPage.razor': '100644 blob e2ffc5942c836b940b9d7582c126b8c340ecef3c',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberListLoaders.cs': '100644 blob b796a9d728c3901450b6e2c69ad98f37de72164c',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOrderCreationContent.razor': '100644 blob 2dcd116a845c75f478caa451a8364589941e0aff',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOrderCreationDisplayModel.cs': '100644 blob f44f1eb95ef825205387b32844d249cf0496c919',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOrderCreationPage.razor': '100644 blob d9cab9da5c1972f53fe827436be5969072bf6a05',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOrderDetailContent.razor': '100644 blob daeb6f34f8b57ef3bea36365cf5e1d5e6fccc328',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOrderDetailDisplayModel.cs': '100644 blob 243ccbeb29ae66e14ad70793468548453ff0472e',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOrderDetailPage.razor': '100644 blob 7ea48cf93ff5e4ea6006a9d90012755053da6d2c',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOrderHistoryContent.razor': '100644 blob 54847e0e7928fbbd556d83831e012d5f8b09ceb9',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOrderHistoryDisplayModel.cs': '100644 blob e7e9adddbb1893f0b56df7dbc97ebf11c0dd9e07',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOrderHistoryPage.razor': '100644 blob 81c1f273665ce09091b778ad0a400baf14e20d69',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOrdersIndexContent.razor': '100644 blob bcd393f1c879aa96eb29978d9512e4b649d57401',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOrdersIndexPage.razor': '100644 blob 054aac28322860dac2eecc7b72a7ba71eb53bbb9',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOverviewContent.razor': '100644 blob df2f63934ba097ba4045bb4b9cbf04084c267306',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOverviewDisplayModel.cs': '100644 blob f0a1ebb1ab0b587fc38acb944104bd128eabf19a',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOverviewLoader.cs': '100644 blob 69d7a26653247779573d9cfc4b692ad103861809',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberOverviewPage.razor': '100644 blob d5b16394750ee2812a5ff7d80a7b28b9b0be50ff',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberProfileContent.razor': '100644 blob 068b4867dcdb346d61f8280e4f1eb640ab00d618',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberProfileDisplayModel.cs': '100644 blob 20dc386c44776d7c1aefb36ba8b55df55fd72317',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberProfilePage.razor': '100644 blob e6c4000ebd66613cd65f32458d3db198b60b2d9b',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberQuotationDetailContent.razor': '100644 blob 4ad4926a9191a8161bad37ef437a680c8af8b4ee',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberQuotationDetailDisplayModel.cs': '100644 blob ba7f0e7f45149ba06d631ec8f18f96319c282b87',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberQuotationDetailPage.razor': '100644 blob f47a8d40390f65f7bccb26b7d6ec679098ead761',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberQuotationsIndexContent.razor': '100644 blob 6154457b90d8c0d0d6c6df1691bf36fdd43810b9',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberQuotationsIndexDisplayModel.cs': '100644 blob 58e01844931f293f2e42a6946a41ed45d68f8702',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberQuotationsIndexPage.razor': '100644 blob 9b38d3fa09e8b77eef7e83d197c48a1fbcdc7876',
 'Legacy.Maliev.Web/Components/Pages/Member/MemberWorkspaceShell.razor': '100644 blob a14de447fc80efd9a785f646c90fae8ff9fd978b',
 'Legacy.Maliev.Web/Components/Pages/Quotation/QuotationContent.cs': '100644 blob 4bd8a5decd7f9ed7794dccc02990e9d1d2f6057c',
 'Legacy.Maliev.Web/Components/Pages/Quotation/QuotationDocumentMetadata.cs': '100644 blob e730ee3b12bad46e0648f4c36734278a27642b5c',
 'Legacy.Maliev.Web/Components/Pages/Quotation/QuotationFormDisplayModel.cs': '100644 blob 84832e95b2765c2c92822a7c31db122125d04696',
 'Legacy.Maliev.Web/Components/Pages/Quotation/QuotationFormFields.razor': '100644 blob 7df25d8d9c9da908d2e4f5fd6e15142323acc2a3',
 'Legacy.Maliev.Web/Components/Pages/Quotation/QuotationGuidanceContent.razor': '100644 blob 47499477b009a5699d9fbb40ca69f6dfd2bc2bec',
 'Legacy.Maliev.Web/Components/Pages/Quotation/QuotationHeroContent.razor': '100644 blob f3d56c2bcfd95243f259d877fc212ccb0ccf7e2a',
 'Legacy.Maliev.Web/Components/Pages/Quotation/QuotationPage.razor': '100644 blob f13936f3febe5d27b982e80dd4416bb4718e7df9',
 'Legacy.Maliev.Web/Components/Pages/Quotation/QuotationPrefill.cs': '100644 blob 4384aa7f243def64c49ce15d37d2bb4244a27ea2',
 'Legacy.Maliev.Web/Components/Pages/Quotation/QuotationTrustedCustomerLoader.cs': '100644 blob 7df1f374c061f3b4e3010f9136c79f61cb236eb3',
 'Legacy.Maliev.Web/Components/Pages/Services/CncMachiningContent.razor': '100644 blob 42c860d46fcdd8599fa3b9fa3f239b763d4dd59a',
 'Legacy.Maliev.Web/Components/Pages/Services/CncMachiningPage.razor': '100644 blob a2dc5f11e3a50b39c53b4d37dd47adfad1d9da29',
 'Legacy.Maliev.Web/Components/Pages/Services/CustomManufacturingContent.razor': '100644 blob 4aec0a05194f0e3e8067dd2a9dfc82368b10f215',
 'Legacy.Maliev.Web/Components/Pages/Services/CustomManufacturingPage.razor': '100644 blob 36c69af4ec0ab149d220a1b2fa3d65cfcaec50fb',
 'Legacy.Maliev.Web/Components/Pages/Services/FinishingAndColorPage.razor': '100644 blob 58debd88a839add869507859907040ef1118d7ce',
 'Legacy.Maliev.Web/Components/Pages/Services/LowVolumeInjectionMoldingContent.razor': '100644 blob 7509f80b3da27bd6872049dc30d2d595cc098f49',
 'Legacy.Maliev.Web/Components/Pages/Services/LowVolumeInjectionMoldingPage.razor': '100644 blob 6f05d0af39c52df9987173f17ec6ba9b895362f6',
 'Legacy.Maliev.Web/Components/Pages/Services/ServicesContent.razor': '100644 blob 58d881a63441e52576f8d05efd3526ed3af9304a',
 'Legacy.Maliev.Web/Components/Pages/Services/ServicesPage.razor': '100644 blob 477fe9379edede2717215f27b5cf14ae34647ff4',
 'Legacy.Maliev.Web/Components/Pages/Services/SiliconeCastingContent.razor': '100644 blob 276151ae7b9dcb6e3f3251760be389acb2eac0ef',
 'Legacy.Maliev.Web/Components/Pages/Services/SiliconeCastingPage.razor': '100644 blob 81936f4d13188f93798597c01963dd758a894fa6',
 'Legacy.Maliev.Web/Components/Pages/Services/ThreeDimensionalDesignContent.razor': '100644 blob 87c812bee30037c6788192a33fd458a4b9ceb791',
 'Legacy.Maliev.Web/Components/Pages/Services/ThreeDimensionalDesignPage.razor': '100644 blob fbd06dde501ab37149ff79b6f1d437e820959cb9',
 'Legacy.Maliev.Web/Components/Pages/Services/ThreeDimensionalPrintingContent.razor': '100644 blob 6eac5f2cdeb4a5566b0c7f701217c4e7512db5c3',
 'Legacy.Maliev.Web/Components/Pages/Services/ThreeDimensionalPrintingPage.razor': '100644 blob e922fc8b69e37435c591666628968e413af072e5',
 'Legacy.Maliev.Web/Components/Pages/Services/ThreeDimensionalScanningContent.razor': '100644 blob de3f81cb362d5f7fb107155988d1f141b124f0bf',
 'Legacy.Maliev.Web/Components/Pages/Services/ThreeDimensionalScanningPage.razor': '100644 blob 0c4457f587bad93503e0b9f79b26fe05d652b417',
 'Legacy.Maliev.Web/Components/Routes.razor': '100644 blob 06570ce63968289304571b574dc30dc1e4dbbf01',
 'Legacy.Maliev.Web/Components/Shared/ServiceBreadcrumb.razor': '100644 blob 00b9ef3cde581ab79208a9c6bddf5a5e0afa2e5a',
 'Legacy.Maliev.Web/Components/Shared/ServiceLocation.razor': '100644 blob 290298e18c53b620f9cbc12eb9979b48d6e0df7d',
 'Legacy.Maliev.Web/Components/Shared/ServicePageToc.razor': '100644 blob b29cc79f61fe88829ff2c990e3b1b9eb76d48b2b',
 'Legacy.Maliev.Web/Components/Shared/ServicePricing.razor': '100644 blob d81f5c0c2164a846ff039dba528174ecc18cd2ef',
 'Legacy.Maliev.Web/CustomerDocumentEndpointRouteBuilderExtensions.cs': '100644 blob 2c3a204ff8a6790dbe6cd697b64b40fdacdddae1',
 'Legacy.Maliev.Web/Dockerfile': '100644 blob 8c60982c02ab4854e61c0db84d9575079671165e',
 'Legacy.Maliev.Web/Infrastructure/TestingRateLimiterLifetime.cs': '100644 blob d44bc567a2bf553773bc91f6f291c9ef2049b33a',
 'Legacy.Maliev.Web/Infrastructure/TestingWebRootLease.cs': '100644 blob c6371f0dbb1b51a99bf50e4373eed6d7c3a5ec00',
 'Legacy.Maliev.Web/Legacy.Maliev.Web.csproj': '100644 blob 7226f0d543247bc73ec4adcbccec7211df644511',
 'Legacy.Maliev.Web/MemberCompatibilityEndpointRouteBuilderExtensions.cs': '100644 blob 5648d0a21bfc2d4c51bd34214b11d0b99156fe00',
 'Legacy.Maliev.Web/Middleware/BuildIdentityHeaderMiddleware.cs': '100644 blob dd445abd719cb87f03040db8e02f0a3e8db66539',
 'Legacy.Maliev.Web/Middleware/ErrorIncidentHandler.cs': '100644 blob f86832f835c574edb3bb130e856fd232bf11fe32',
 'Legacy.Maliev.Web/Middleware/ErrorResponseContractMiddleware.cs': '100644 blob 6d7339ddae55a9c002ca9b6966a22377ae70d88e',
 'Legacy.Maliev.Web/Middleware/QuotationRouteRedirectMiddleware.cs': '100644 blob 667e7f8fb9b3b72d9580867e1e8ca253303fc762',
 'Legacy.Maliev.Web/Middleware/WebContentSecurityPolicyMiddleware.cs': '100644 blob 3ec7f6a396ce298226531266982eaf373db6e859',
 'Legacy.Maliev.Web/Pages/About/Index.cshtml': '100644 blob b7f642f027b505223a96a2ec2b4083d25c8c0070',
 'Legacy.Maliev.Web/Pages/About/Index.cshtml.cs': '100644 blob 6a7d720353da33151d5ee983f881a8b38f3a8020',
 'Legacy.Maliev.Web/Pages/About/SocialMedia.cshtml': '100644 blob dd65cf41d83a43a1612ca1546319dcf1f38ed9ef',
 'Legacy.Maliev.Web/Pages/About/SocialMedia.cshtml.cs': '100644 blob 8690002a7d3d7cc3338c93757ac5241124a12efd',
 'Legacy.Maliev.Web/Pages/Account/AccessDenied.cshtml': '100644 blob b73c0c141189ada8e61ab58a5a03ddd20c242e8b',
 'Legacy.Maliev.Web/Pages/Account/AccessDenied.cshtml.cs': '100644 blob dfd7d694bc94207135239e8e50e121556c2b7556',
 'Legacy.Maliev.Web/Pages/Account/ChangeEmailConfirmation.cshtml': '100644 blob 0d0e2dc3f84aabade33c3055c532c8664b53d2fa',
 'Legacy.Maliev.Web/Pages/Account/ChangeEmailConfirmation.cshtml.cs': '100644 blob bc14c88af6acd87c7220a81ae349a7b90d25afcb',
 'Legacy.Maliev.Web/Pages/Account/EmailConfirmation.cshtml': '100644 blob 15a122c234d3581b6e1bc8058f1d06f178eea95e',
 'Legacy.Maliev.Web/Pages/Account/EmailConfirmation.cshtml.cs': '100644 blob cb228c576b11a141e3d1e4109519ebf638ca0b13',
 'Legacy.Maliev.Web/Pages/Account/ForgotPassword.cshtml': '100644 blob 97e3d8d13ad9836b9121683ff8d942e900fa6ed1',
 'Legacy.Maliev.Web/Pages/Account/ForgotPassword.cshtml.cs': '100644 blob c5d5ec542aad2025eff6ede0e80e755053a69d93',
 'Legacy.Maliev.Web/Pages/Account/Index.cshtml': '100644 blob 3b0c169fe4749d04a2c31c1dc44ab894ab16608b',
 'Legacy.Maliev.Web/Pages/Account/Index.cshtml.cs': '100644 blob 6198885b3132021bb01cfc7a3db3be9a43bb1d86',
 'Legacy.Maliev.Web/Pages/Account/Login.cshtml': '100644 blob cdad5e5d3705342793ac88c5828ced412b80d4a9',
 'Legacy.Maliev.Web/Pages/Account/Login.cshtml.cs': '100644 blob 25ff24b5fad1ea24516149fb6494540037ca75b2',
 'Legacy.Maliev.Web/Pages/Account/Logout.cshtml': '100644 blob 28bf51096c54422accfa57d7cbbf5b5c88f7091f',
 'Legacy.Maliev.Web/Pages/Account/Logout.cshtml.cs': '100644 blob ba3926908c2f5e4766c458a02a2619e4b9969ab2',
 'Legacy.Maliev.Web/Pages/Account/ResetPassword.cshtml': '100644 blob c3a1519b897c4f617fd226f72c0d8536d0a34411',
 'Legacy.Maliev.Web/Pages/Account/ResetPassword.cshtml.cs': '100644 blob e400912c83c3a5f724081277a90ae6de7e937295',
 'Legacy.Maliev.Web/Pages/Account/SetInitialPassword.cshtml': '100644 blob 8b2cc4f31aca2c0d7c9fe042b466ae6de747122e',
 'Legacy.Maliev.Web/Pages/Account/SetInitialPassword.cshtml.cs': '100644 blob 60b72730b3ac18c24073b892518d9aef5571013d',
 'Legacy.Maliev.Web/Pages/Account/Signup.cshtml': '100644 blob 9e0e94c616a9d1c39f5f9e74f5cd587cff157683',
 'Legacy.Maliev.Web/Pages/Account/Signup.cshtml.cs': '100644 blob be33f3212967612c2f8153ab6883c69f1431daac',
 'Legacy.Maliev.Web/Pages/Career/Index.cshtml': '100644 blob 6ab92b8773fe66e9d31eac98ec1e3ee4160d63e2',
 'Legacy.Maliev.Web/Pages/Career/Index.cshtml.cs': '100644 blob c42242675b3eb2a0090667f7344b2fa3485c2638',
 'Legacy.Maliev.Web/Pages/Career/View.cshtml': '100644 blob 22124819e1328462f0c71753551bd2bc944fcce5',
 'Legacy.Maliev.Web/Pages/Career/View.cshtml.cs': '100644 blob 101f75963ce20953227b03a3c6ddef9dba888778',
 'Legacy.Maliev.Web/Pages/Contact/Index.cshtml': '100644 blob 544a940073389d1a9f7e79fc3219bf54fb50f387',
 'Legacy.Maliev.Web/Pages/Contact/Index.cshtml.cs': '100644 blob d95cb8538eda2bfeddc65fc1a491d420de6a3831',
 'Legacy.Maliev.Web/Pages/Error.cshtml': '100644 blob d86a717095dccd32576d96bfcebfd93ae2cfb31b',
 'Legacy.Maliev.Web/Pages/Error.cshtml.cs': '100644 blob 761ca223c0bbdecb8641c4104377835a2c4505d2',
 'Legacy.Maliev.Web/Pages/Index.cshtml': '100644 blob d791b589e4ff71b4cc4e1a88e155b45e891c26ca',
 'Legacy.Maliev.Web/Pages/Index.cshtml.cs': '100644 blob 9ae2ab201d5c91bcd0175182770e6c88bb628cf7',
 'Legacy.Maliev.Web/Pages/InstantQuotation/3D-Printing.cshtml': '100644 blob dea78a3e352960287060230bfc3c17ce8ce372b2',
 'Legacy.Maliev.Web/Pages/InstantQuotation/3D-Printing.cshtml.cs': '100644 blob e6ba31d5df2fcb2da21dd455933d7283aa9d18bd',
 'Legacy.Maliev.Web/Pages/InstantQuotation/CNC-Machining.cshtml': '100644 blob 1119b593f0cb934f41ad5190b7551a8f3963dacc',
 'Legacy.Maliev.Web/Pages/InstantQuotation/CNC-Machining.cshtml.cs': '100644 blob 3d36cdde09ef175620bc6a8ccc496536c3d976fc',
 'Legacy.Maliev.Web/Pages/Legal/Index.cshtml': '100644 blob fbb22eeeb25442d572272ce896999bd82ebd08a0',
 'Legacy.Maliev.Web/Pages/Legal/Index.cshtml.cs': '100644 blob 9c56a06c4c736339d9095e0404ae892c446a9177',
 'Legacy.Maliev.Web/Pages/Legal/NoWeapons.cshtml': '100644 blob b69faccec4c382ab9f5e60fc9cf3306153822157',
 'Legacy.Maliev.Web/Pages/Legal/NonDisclosureAgreement.cshtml': '100644 blob 0d8db4bbd2d226bfa05255e12bb1229bff6609d6',
 'Legacy.Maliev.Web/Pages/Legal/NonDisclosureAgreement.cshtml.cs': '100644 blob 66248d77f402154fc9b2d6af84e39c1011cb37eb',
 'Legacy.Maliev.Web/Pages/Legal/PrivacyPolicy.cshtml': '100644 blob dd1d261ffda42bf19ed83fdd6700493019cc45df',
 'Legacy.Maliev.Web/Pages/Legal/PrivacyPolicy.cshtml.cs': '100644 blob 2db9276142e67c1d11bc95d93112f96f721dd9fc',
 'Legacy.Maliev.Web/Pages/Legal/TermsConditions.cshtml': '100644 blob ddf7c3c6a93cf798e6c1352808920a93a2d3f29e',
 'Legacy.Maliev.Web/Pages/Legal/TermsConditions.cshtml.cs': '100644 blob f29ab9f0f3251ac1a2bdd17006c0c9043c1338e8',
 'Legacy.Maliev.Web/Pages/PublicSearchRouteCatalog.cs': '100644 blob d884f8aaecb01d2b88f2f96bf1ce301610ec7284',
 'Legacy.Maliev.Web/Pages/Quotation/FinishingColorQuotePrefill.cs': '100644 blob 768fc0415023d79de54ac8c39ba12979afc7d9ba',
 'Legacy.Maliev.Web/Pages/Quotation/Index.cshtml': '100644 blob ea9d9dd555c5ac92a5f9cdf464b418eba8b3144a',
 'Legacy.Maliev.Web/Pages/Quotation/Index.cshtml.cs': '100644 blob 5053c4679c43fe464bde0d4b7ee9d498a1e23353',
 'Legacy.Maliev.Web/Pages/Services/3D-Printing.cshtml': '100644 blob 531f6963ab0d757d1acfbe6d5718c3d28198eec1',
 'Legacy.Maliev.Web/Pages/Services/3D-Printing.cshtml.cs': '100644 blob ee690424182adbe6074d0a7615bb332b55fb4f22',
 'Legacy.Maliev.Web/Pages/Services/3D-Scanning.cshtml': '100644 blob f1d4f034df9ccd11350709c58264ffc0d50ad737',
 'Legacy.Maliev.Web/Pages/Services/3D-Scanning.cshtml.cs': '100644 blob b2f9c82d359b7ed684663c07fa1ce66e0630448d',
 'Legacy.Maliev.Web/Pages/Services/CNC-Machining.cshtml': '100644 blob cceb0e4d54b2197f93e661e47a7b5c019fb33a7f',
 'Legacy.Maliev.Web/Pages/Services/CNC-Machining.cshtml.cs': '100644 blob df68425cf1d318d08f0501daa294a5005faac613',
 'Legacy.Maliev.Web/Pages/Services/Custom-Manufacturing.cshtml': '100644 blob cd9b03ef766549da9ffaaffd6a14d3eb816229eb',
 'Legacy.Maliev.Web/Pages/Services/Custom-Manufacturing.cshtml.cs': '100644 blob 2b5ad50c5e635db501b222601db748c99064c186',
 'Legacy.Maliev.Web/Pages/Services/Index.cshtml': '100644 blob a879f716f4166cc38724a6dbb4d562d2ba7c4e3a',
 'Legacy.Maliev.Web/Pages/Services/Index.cshtml.cs': '100644 blob 824d82efadd1623638b528b98a86d3f3c5bb8a9c',
 'Legacy.Maliev.Web/Pages/Shared/CareerSearchCompatibilityEndpoint.cs': '100644 blob f12abd55cfcc86ff93912479720aec304d22ab93',
 'Legacy.Maliev.Web/Pages/Shared/CustomerJourneyAnalyticsEventQueue.cs': '100644 blob 3f1a3faa0f79e58529b9a295ed9b540efc1d818b',
 'Legacy.Maliev.Web/Pages/Shared/GoogleAnalyticsUserId.cs': '100644 blob 795ee02113a445cd48a5dfa708c6ac50a2030030',
 'Legacy.Maliev.Web/Pages/Shared/LeadAnalyticsEventQueue.cs': '100644 blob eb5e644552d1d296641872c86e27fad9825070ee',
 'Legacy.Maliev.Web/Pages/Shared/PageSizeQuery.cs': '100644 blob 9cd3880ec262babe04fadfbb173dde2491fd16b1',
 'Legacy.Maliev.Web/Pages/Shared/ServiceFinderAttribution.cs': '100644 blob dbecfbeb23016f36aa78606c12c694e11c4d0b25',
 'Legacy.Maliev.Web/Pages/Shared/ServiceFinderMetadataEnvelope.cs': '100644 blob 6ffd61fe8ee62c175d268baaee6f3549f9dcebc1',
 'Legacy.Maliev.Web/Pages/Shared/_ContactUsSectionPartial.cshtml': '100644 blob 4833ba8c27162f24ecf5d3d2f08aaf32720ebeb0',
 'Legacy.Maliev.Web/Pages/Shared/_CssPartial.cshtml': '100644 blob c6f4c48bfd6fbe2a5e6b1425ef7bc584dc688ee3',
 'Legacy.Maliev.Web/Pages/Shared/_FaviconsPartial.cshtml': '100644 blob 84b07575c9754550c6e1e644e7310bfc899a2172',
 'Legacy.Maliev.Web/Pages/Shared/_FontPartial.cshtml': '100644 blob e2e8c0a6ca4df69529cbaf1ffc198388d97345d9',
 'Legacy.Maliev.Web/Pages/Shared/_Go2TopPartial.cshtml': '100644 blob f540c99d762d07f1dc59f990c80d6653df383eac',
 'Legacy.Maliev.Web/Pages/Shared/_InstantQuotationLayout.cshtml': '100644 blob 7a13f9bfa79147b894789d3ca99aec0d9c12a009',
 'Legacy.Maliev.Web/Pages/Shared/_JavaScriptsPartial.cshtml': '100644 blob 4f76e0b7745a6215a9631a1611a278fb22859fc8',
 'Legacy.Maliev.Web/Pages/Shared/_Layout.cshtml': '100644 blob 37d0b297a546530fb77cc078ea04e07b6cde5b2c',
 'Legacy.Maliev.Web/Pages/Shared/_NotFoundLayout.cshtml': '100644 blob a6173862655dd720ac1ed31dd8e9e09fbbaa6d1c',
 'Legacy.Maliev.Web/Pages/Shared/_ReportBugPartial.cshtml': '100644 blob ec4442d8718ecb154c952ece6d9757acb6627d1c',
 'Legacy.Maliev.Web/Pages/Shared/_SchemaFAQ.cshtml': '100644 blob eb86a4e3641f8de73e7e8304bed3f0ec4a19afef',
 'Legacy.Maliev.Web/Pages/Shared/_SelectLanguageFooterPartial.cshtml': '100644 blob 75899fb8c358bdd7e5b4735e517ebe2d61b918e1',
 'Legacy.Maliev.Web/Pages/Shared/_SupportedPaymentsPartial.cshtml': '100644 blob 0d3b3ed4ac33597669f3c2fb0e06208ef30329d5',
 'Legacy.Maliev.Web/Pages/Shared/_SupportedShippingCouriersPartial.cshtml': '100644 blob cefe264d9f740f654228460a383527f97f99bb65',
 'Legacy.Maliev.Web/Pages/Shared/_ThreeJsJavascriptsPartial.cshtml': '100644 blob 91231a13bbe1168603cc1006fe3616471924ba3f',
 'Legacy.Maliev.Web/Pages/Shared/_ValidationScriptsPartial.cshtml': '100644 blob 06f64603d4893d95dccb4f8008426ea1e11f3cf8',
 'Legacy.Maliev.Web/Pages/_ViewImports.cshtml': '100644 blob bef273c62288c8d212d6fd8965c790fdf07dfda0',
 'Legacy.Maliev.Web/Pages/_ViewStart.cshtml': '100644 blob 820a2f6e02f108554a7392e866561bf791a3d360',
 'Legacy.Maliev.Web/Program.cs': '100644 blob 2b4f3d04076673687e49342bd89cd265f686f6a9',
 'Legacy.Maliev.Web/Properties/AssemblyInfo.cs': '100644 blob 2bd1e87896894f2c3f39f87fc4d83651774afe04',
 'Legacy.Maliev.Web/Resources/Areas/Knowledges/Pages/Shared/_LayoutKnowledges.th.resx': '100644 blob 684787afa734d004893724e05c7b8c13e71f7082',
 'Legacy.Maliev.Web/Resources/Areas/Knowledges/Pages/Shared/_SidebarPartial.th.resx': '100644 blob 5186075e8005187a30e1ffaafddc6542e257dda4',
 'Legacy.Maliev.Web/Resources/Areas/Member/Pages/Account/ChangeEmailConfirmation.th.resx': '100644 blob 8560f657f5a753680a72e5f3bab884801f3911ad',
 'Legacy.Maliev.Web/Resources/Areas/Member/Pages/Account/EmailConfirmation.th.resx': '100644 blob d3feb8a2bae08c8bb173fc5b2dc2be9e2be424ab',
 'Legacy.Maliev.Web/Resources/Areas/Member/Pages/Account/ForgotPassword.th.resx': '100644 blob e1accbb26ea6e35ff83af1555c9315d42c859787',
 'Legacy.Maliev.Web/Resources/Areas/Member/Pages/Account/Manage/CreatePassword.th.resx': '100644 blob 2d36c7bbd9a3fe966ffc5ce1bcf0fadf6a3ea400',
 'Legacy.Maliev.Web/Resources/Areas/Member/Pages/Account/ResetPassword.th.resx': '100644 blob 6ad4e7e0354d7f5e2eeb825b0b9aeba278e3c532',
 'Legacy.Maliev.Web/Resources/Areas/Member/Pages/Orders/3D-Printing.th.resx': '100644 blob 806ac2a37996f0dafd6611525d978cceac7e703f',
 'Legacy.Maliev.Web/Resources/Areas/Member/Pages/Orders/3D-Scanning.th.resx': '100644 blob 825fe191c0258ad03be874dc1bdc77bef6d363c7',
 'Legacy.Maliev.Web/Resources/Areas/Member/Pages/Orders/CNC-Machining.th.resx': '100644 blob 95eeb1591f8ddebd815ac648218c781b1343fd98',
 'Legacy.Maliev.Web/Resources/Areas/Member/Pages/Shared/_LayoutMember.th.resx': '100644 blob 0094dd205942c975f941f36b3cc0af88197fdae7',
 'Legacy.Maliev.Web/Resources/Areas/Member/Pages/Shared/_SidebarPartial.th.resx': '100644 blob b56fe5d396bc9a754ced0d0044551202ac19b6ba',
 'Legacy.Maliev.Web/Resources/Components/App.th.resx': '100644 blob 899357ab3f90ee45df9486d88b410fc9583640fd',
 'Legacy.Maliev.Web/Resources/Components/Layout/PublicBrowserCompatibilityNotice.th.resx': '100644 blob bbf3b8558b08ef54d7f63dd087d0f727d7f4332e',
 'Legacy.Maliev.Web/Resources/Components/Layout/PublicCookieConsent.th.resx': '100644 blob 0681eebfcd2283ac72f0eedc41df44f612fc1a02',
 'Legacy.Maliev.Web/Resources/Components/Layout/PublicFooter.th.resx': '100644 blob f8cdb152baf8c6aa14bfc55bbce9219b6a1b7b04',
 'Legacy.Maliev.Web/Resources/Components/Layout/PublicNavigation.th.resx': '100644 blob e682bf7b9b1e23965642fffdf851b47e8c5d9876',
 'Legacy.Maliev.Web/Resources/Components/Layout/SocialLinks.th.resx': '100644 blob b8bc2f1dc0b4c647465aca86e997a039cc69ae71',
 'Legacy.Maliev.Web/Resources/Components/Lookups/ThaiLookup.th.resx': '100644 blob 00564c1bcca2f2ae5eede45397aaf8858800fb98',
 'Legacy.Maliev.Web/Resources/Components/Pages/About/AboutContent.resx': '100644 blob d1fa3b36ae101e46859b85aeac13f317d187123e',
 'Legacy.Maliev.Web/Resources/Components/Pages/About/AboutContent.th.resx': '100644 blob 8a9334f8c79cc5cdc5b4b3a097e30fb7688b8509',
 'Legacy.Maliev.Web/Resources/Components/Pages/About/SocialMediaContent.th.resx': '100644 blob 467293a2b040b6ca7fc8506e46c002258cc81a24',
 'Legacy.Maliev.Web/Resources/Components/Pages/Account/AccessDeniedContent.th.resx': '100644 blob 4971ff98e22a58a163146a5a39bde91a78420f77',
 'Legacy.Maliev.Web/Resources/Components/Pages/Account/AccountIndexContent.th.resx': '100644 blob 6e753ddea06eda4c9e74f1c5025e9d4a2dd10e1a',
 'Legacy.Maliev.Web/Resources/Components/Pages/Account/ChangeEmailConfirmationContent.th.resx': '100644 blob '
                                                                                                'd1bb6f77fe144446534e0b2e2592db0093538f8d',
 'Legacy.Maliev.Web/Resources/Components/Pages/Account/EmailConfirmationContent.th.resx': '100644 blob 4c99688ecc6e15f91511542e6cb36a5cc39b14ce',
 'Legacy.Maliev.Web/Resources/Components/Pages/Account/ForgotPasswordContent.th.resx': '100644 blob 2b80684de10a9eb1339e117521190379a5c630c3',
 'Legacy.Maliev.Web/Resources/Components/Pages/Account/LoginContent.th.resx': '100644 blob 991269b1dfdbf333bd6439e8552869657a841ece',
 'Legacy.Maliev.Web/Resources/Components/Pages/Account/LogoutContent.th.resx': '100644 blob e12d0768152ceaa634da2170bfa1cb809933fd20',
 'Legacy.Maliev.Web/Resources/Components/Pages/Account/ResetPasswordContent.th.resx': '100644 blob ddc24beaeeaa1596a687e12e356011b451ad5eea',
 'Legacy.Maliev.Web/Resources/Components/Pages/Account/SetInitialPasswordContent.th.resx': '100644 blob 332e8128af18612b1d54a7a416036c7213197313',
 'Legacy.Maliev.Web/Resources/Components/Pages/Account/SignupContent.th.resx': '100644 blob 5bc48797e9c5b53e99de6692d668a91665c5e7f1',
 'Legacy.Maliev.Web/Resources/Components/Pages/Career/CareerDetailContent.th.resx': '100644 blob f1b172cfdb5ada0e0b3488ee46080796679d76e5',
 'Legacy.Maliev.Web/Resources/Components/Pages/Career/CareerIndexContent.th.resx': '100644 blob a052f36e6852fb16bb2af7c525a3bc3ef23b9629',
 'Legacy.Maliev.Web/Resources/Components/Pages/Contact/ContactContent.th.resx': '100644 blob 2c960fda7bc0f34b1148daa547d78551d259c8c1',
 'Legacy.Maliev.Web/Resources/Components/Pages/ErrorContent.th.resx': '100644 blob 878d01f30e0a570c5f5ca82fbe08d898f8057d1f',
 'Legacy.Maliev.Web/Resources/Components/Pages/Home/HomeContent.resx': '100644 blob eae57adb238c20681c80dc2fe0c434a55c255f4c',
 'Legacy.Maliev.Web/Resources/Components/Pages/Home/HomeContent.th.resx': '100644 blob 730f5483ab329007a459056b3a1eb4ede00e5ba5',
 'Legacy.Maliev.Web/Resources/Components/Pages/Home/HomePage.th.resx': '100644 blob 5ba650ad91535eafda16aa72ca9e2768b16f2517',
 'Legacy.Maliev.Web/Resources/Components/Pages/InstantQuotation/ThreeDimensionalPrintingEstimateContent.th.resx': '100644 blob '
                                                                                                                  '2aac16745f182bdc680dd81248374fd6ebadedb5',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/CncCuttingToolsContent.th.resx': '100644 blob d1218c36b197d511e0e3e41fc7b7135a7ace4833',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/CncFeedsAndSpeedsContent.th.resx': '100644 blob 993329b98ee77dd2974f181934420c65baa062d4',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/CncGeometryAndWorkholdingContent.th.resx': '100644 blob '
                                                                                                     '34bec36e81aa2c16563800eb7288414ae309cea0',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/CncHolesAndTolerancesContent.th.resx': '100644 blob '
                                                                                                 'e5a28faef7feb7c4c89ca78319f55976a8880684',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/CncMachiningProcessesContent.th.resx': '100644 blob '
                                                                                                 '48adc8a8ec4c4997b70ff8d8f1cbaed9b7f101cd',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/FinishingHeatTreatmentContent.th.resx': '100644 blob '
                                                                                                  '27e69d8349913909de9b62b7bf526f202ba348ca',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/FinishingSurfaceFinishesContent.th.resx': '100644 blob '
                                                                                                    '8a1bc948a7bedbd38f8282ea886069bd09ceb3ec',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/GuidelinesContent.th.resx': '100644 blob 46b31477227641e5786f233634115fa781a22ffb',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/InspectionMeasuringPartsContent.th.resx': '100644 blob '
                                                                                                    'e4b3004c3a2e8fce795e26682faab54c07f9a0d0',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/KnowledgeIndexContent.th.resx': '100644 blob b306ff4abe0c886099380ea3255158f5d032227d',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/KnowledgeIndexPage.th.resx': '100644 blob 8754915cb1ce8947684c6ec9405b952c72245aee',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/KnowledgeNavigation.th.resx': '100644 blob 6b6f427114a464eea72f77f5a092e029a4fa8b9b',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/KnowledgeShell.th.resx': '100644 blob c4893d085aa817e9577a1714f76efd9553002e12',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/ManufacturingProcessSelectionContent.th.resx': '100644 blob '
                                                                                                         '5907bfe3f9083ef3efba38918add44d10b3a7809',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/MaterialsEngineeringPlasticsContent.th.resx': '100644 blob '
                                                                                                        'f1b6c13863ac3ab46c6cec77cf9858383b0d6a61',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/MaterialsMetalsContent.th.resx': '100644 blob 6b6295a83d3e0d51b1a85c64923e193eb494ad53',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/MaterialsSelectionContent.th.resx': '100644 blob 8ba3330534aefe053273e9c680dd37c7e905cef9',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/Specifications/CncMachiningContent.th.resx': '100644 blob '
                                                                                                       '0b977612dedb71addc2c46ae84624f345977e5cb',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/Specifications/CncMachiningSpecificationPage.th.resx': '100644 blob '
                                                                                                                 '7aab6b13b6f9ebae0b5b5055ed920bfb96cad9ae',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/Specifications/SpecificationsIndexContent.th.resx': '100644 blob '
                                                                                                              'e70dc0c35086630d516ba1a6801b4d71d882df89',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/Specifications/SpecificationsIndexPage.th.resx': '100644 blob '
                                                                                                           '2f5bbc455880b769fd2f64f54a5c993f9d15289f',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/Specifications/ThreeDimensionalPrintingContent.th.resx': '100644 blob '
                                                                                                                   'dc1874d8cf4ba051ed2b28cacea43d8d695e6bc4',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/Specifications/ThreeDimensionalPrintingSpecificationPage.th.resx': '100644 blob '
                                                                                                                             'de97b23989d2a5467804682f5c2702064db06750',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/Specifications/ThreeDimensionalScanningContent.th.resx': '100644 blob '
                                                                                                                   'ef615947acf8473f5f4d6b690325e5d29898b0b5',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/Specifications/ThreeDimensionalScanningSpecificationPage.th.resx': '100644 blob '
                                                                                                                             '76e6bd668a7070e0d3360962e4121a8e48ee31f1',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/WorkflowContent.th.resx': '100644 blob 31e1dab8198dd4c6eb93d47e2b9c4638c986ee33',
 'Legacy.Maliev.Web/Resources/Components/Pages/Knowledges/WorkflowPage.th.resx': '100644 blob e2f82c8f2db5f7ee0fce5aec915c2ec5ee1f454c',
 'Legacy.Maliev.Web/Resources/Components/Pages/Legal/LegalContent.th.resx': '100644 blob 499343afd7e6972e8d4798ad54c8df46727231c4',
 'Legacy.Maliev.Web/Resources/Components/Pages/Legal/NonDisclosureAgreementContent.th.resx': '100644 blob bcbd5a256b1fc837ca6171780a7b3ba2f2491a9a',
 'Legacy.Maliev.Web/Resources/Components/Pages/Legal/PrivacyPolicyContent.th.resx': '100644 blob e1dbcf0fd14855105a13f5cff3f63cd9d676aeec',
 'Legacy.Maliev.Web/Resources/Components/Pages/Legal/TermsConditionsContent.th.resx': '100644 blob 6f2c99391cddc100c9ab7a0e70fc5ae603e36cb2',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberAccountIndexContent.th.resx': '100644 blob cc29ca4d16e646f6b151de8ae8310226938e3bea',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberAddressContent.th.resx': '100644 blob b1fe6d073ee5d179c51c18d1c105903cf574e00b',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberChangeEmailContent.th.resx': '100644 blob c84bf7fe0fcbe01f2bff1c96eb38c1207760f63e',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberChangePasswordContent.th.resx': '100644 blob 845236a7e8906f0fa86a653d29df8fbdb5024add',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberCreatePasswordContent.th.resx': '100644 blob 36f0b6a0742c0fe03674e48675cec72525712f52',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberDocumentsContent.th.resx': '100644 blob e428e3eeb95bd20e21f3b141dab2ac379d25abfc',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberOrderCreationContent.th.resx': '100644 blob b4b37e4f4e2099a9199097b7fdfc6424ab1f7b58',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberOrderDetailContent.th.resx': '100644 blob 84163c7b510da93b66411719d792778c7cc328b0',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberOrderHistoryContent.th.resx': '100644 blob 1c924b87a683bdd0117ac7a3cd4561b9a7044fc7',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberOrdersIndexContent.th.resx': '100644 blob d3396ed4e74651818dbbb20df6f2c1d8f6486948',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberOverviewContent.th.resx': '100644 blob 47d355dd6766c949d9c9ef2a91366d93155bef82',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberProfileContent.th.resx': '100644 blob b0ad065e9c37c4bd702e7a287412e0f0ec1c770c',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberQuotationDetailContent.th.resx': '100644 blob d25d074a410319921806c114d50cec8b702a2441',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberQuotationsIndexContent.th.resx': '100644 blob a3c385512a57341e4014d901479b3c6567eb95ce',
 'Legacy.Maliev.Web/Resources/Components/Pages/Member/MemberWorkspaceShell.th.resx': '100644 blob 0626a1ab680fdf46df7d9733a429e5ac3e5e2eb6',
 'Legacy.Maliev.Web/Resources/Components/Pages/Quotation/QuotationContent.th.resx': '100644 blob 70db1113d54d5b0ee94730b875726adf9a72f73c',
 'Legacy.Maliev.Web/Resources/Components/Pages/Services/ServicesContent.th.resx': '100644 blob 78740b76ee99401649370ea486634651b6e44f40',
 'Legacy.Maliev.Web/Resources/Components/Pages/Services/ServicesPage.th.resx': '100644 blob 1748ce36922e2c54cdcc62e5518aee4ffd366313',
 'Legacy.Maliev.Web/Resources/Pages/About/SocialMedia.th.resx': '100644 blob d02a87c5e72ed0a663674a52f0118e4f0907fe69',
 'Legacy.Maliev.Web/Resources/Pages/InstantQuotation/ThreeDimensionalPrinting.en.resx': '100644 blob f5ecefcdc73c308e13c985f751b7871ab9a2235d',
 'Legacy.Maliev.Web/Resources/Pages/InstantQuotation/ThreeDimensionalPrinting.th.resx': '100644 blob 38d2aebdfcca5c81eabc18d3bd27299712e0f9c0',
 'Legacy.Maliev.Web/Resources/Pages/Legal/Index.th.resx': '100644 blob 99d07372417ac2bedd1341d323bbbd8c9a81a17d',
 'Legacy.Maliev.Web/Resources/Pages/ModelViewer.th.resx': '100644 blob 592b63d6df1ae6d6a267de4f201cc9bb2c8e390e',
 'Legacy.Maliev.Web/Resources/Pages/Services/3D-Printing.th.resx': '100644 blob 11d1aa39a59509bbeb436080abca116ab23455ba',
 'Legacy.Maliev.Web/Resources/Pages/Services/3D-Scanning.th.resx': '100644 blob c94373f6fe10e6aadb3d7181c1a2b6eeb9035161',
 'Legacy.Maliev.Web/Resources/Pages/Services/CNC-Machining.th.resx': '100644 blob 4a1f45bac0f6c66e6125a6832bafa2bbcd974a20',
 'Legacy.Maliev.Web/Resources/Pages/Services/Index.th.resx': '100644 blob a6735ca5f6128688a4f1629f5471f0acc1faf389',
 'Legacy.Maliev.Web/Resources/Pages/Services/Lithophane/Index.th.resx': '100644 blob 4b0279e7980a0ebfaf8b0985688411dc208f3d93',
 'Legacy.Maliev.Web/Resources/Pages/Shared/_ContactUsSectionPartial.th.resx': '100644 blob 85b47aa9c46fd019dd7f77c096c027a4d94c2503',
 'Legacy.Maliev.Web/Resources/Pages/Shared/_Layout.th.resx': '100644 blob 225e764db2117c3d46462f51dc8012516da11701',
 'Legacy.Maliev.Web/Resources/Pages/Shared/_NavigationPartial.resx': '100644 blob a1f0f9a40d4dd102886709e6357fb48629a15497',
 'Legacy.Maliev.Web/Resources/Pages/Shared/_SupportedPaymentsPartial.th.resx': '100644 blob e8f77bf5d6be3c9dde00a0b2080d157728a281da',
 'Legacy.Maliev.Web/Resources/Pages/Shared/_SupportedShippingCouriersPartial.th.resx': '100644 blob 434b575c9ba46792fc784a773b832320cf0ad692',
 'Legacy.Maliev.Web/Services/AdditiveQuoteTicketService.cs': '100644 blob 89b98a0ba11c450c2d0781fa3cec2610e2c36771',
 'Legacy.Maliev.Web/SitemapEndpointRouteBuilderExtensions.cs': '100644 blob 8d356ec99089c0aec6ee7f3c4329448b6e0c9afd',
 'Legacy.Maliev.Web/ThaiLookupEndpoints.cs': '100644 blob ce89f2b4b3c875c62ae0df9ee5973f7c3255e223',
 'Legacy.Maliev.Web/_Imports.razor': '100644 blob cad0009d71da58c8d99c4dda078647f8718cc5a7',
 'Legacy.Maliev.Web/appsettings.Development.json': '100644 blob f2ce5f700c17e4f0e804a1fee16ee89b88993e2c',
 'Legacy.Maliev.Web/appsettings.json': '100644 blob e26b77bca480472eb4776a92e61561b8bda2b296',
 'Legacy.Maliev.Web/assets/app-entry.js': '100644 blob 9bc66de3c28710ed8f435d1b2590ee305625dafe',
 'Legacy.Maliev.Web/assets/finishing-three-runtime.js': '100644 blob 82f94b5622c2e561813e10845374b214f91f5a8d',
 'Legacy.Maliev.Web/assets/route-about.css': '100644 blob ef6f21867be4984ca599b4560e86ccc4ca36c8a3',
 'Legacy.Maliev.Web/assets/route-about.js': '100644 blob 2d4c1df7b95e5449ff2d4d2512418f22bb449750',
 'Legacy.Maliev.Web/assets/route-error.css': '100644 blob 785cc8765d4dc7d46086c73579b60a444050eafe',
 'Legacy.Maliev.Web/assets/route-error.js': '100644 blob daa79bc40ac4ab4c3cbcda97dd31fe66351435f4',
 'Legacy.Maliev.Web/assets/route-home.css': '100644 blob 052a33d5ca35d2e62d396cba2fd5ca451f87e2d6',
 'Legacy.Maliev.Web/assets/route-home.js': '100644 blob 9f544fce0b9f574e74ef9721ef0b5048f6660221',
 'Legacy.Maliev.Web/assets/route-inquiry.css': '100644 blob f8b6cc0cf229f7b2976248831c2a597c5aea3666',
 'Legacy.Maliev.Web/assets/route-inquiry.js': '100644 blob 69a05ac7ab445f63b5c946daacc3ca509cd32ccc',
 'Legacy.Maliev.Web/assets/route-instant-quotation.css': '100644 blob c084f468c2594c46276e5aa57d56ea538cf606cb',
 'Legacy.Maliev.Web/assets/route-instant-quotation.js': '100644 blob c0ad0126b6b7eafd7d42775e63a099494f4062b9',
 'Legacy.Maliev.Web/assets/route-knowledge.js': '100644 blob 71b7686ea887a3ccfbfd1a37d7f7fa544346e2df',
 'Legacy.Maliev.Web/assets/route-member-order.js': '100644 blob 5d6764cfe714d13e37d4b9f9f56d059290857302',
 'Legacy.Maliev.Web/assets/route-service-cnc.js': '100644 blob 20234388126eb49261b05d8ce8ebfde295ba4bc0',
 'Legacy.Maliev.Web/assets/route-service-finder.js': '100644 blob eadcb129a12d1277478417fee2c84a78abe1abd8',
 'Legacy.Maliev.Web/assets/route-service-finishing.js': '100644 blob 0f18b0128c07a71264cdbe7be36ce52ed71df2ca',
 'Legacy.Maliev.Web/assets/route-service-printing.js': '100644 blob 0f3409fd9c961ee5084248d2d26bed1426868e6a',
 'Legacy.Maliev.Web/assets/route-service-scanning.js': '100644 blob 2c91909043eb0edc1802f34f4433b2fabf75ec6d',
 'Legacy.Maliev.Web/assets/route-service-toc.js': '100644 blob f699975da027c9fa1ac854b8c8b3c0f21ed7b7ed',
 'Legacy.Maliev.Web/assets/route-services-index.css': '100644 blob 2c21d8cd1d6d18ead03620347468833ba96dfda7',
 'Legacy.Maliev.Web/assets/route-services.css': '100644 blob 41b5181ebd31081b459a9869fa912e6023c42d4b',
 'Legacy.Maliev.Web/assets/site-entry.css': '100644 blob 451435db618125a6526e53320e6c8b2c1963b4ee',
 'Legacy.Maliev.Web/assets/vendor-entry.js': '100644 blob 0f34cb2900bfcfcf635a7c5d192a03d41adfea20',
 'Legacy.Maliev.Web/build-assets.mjs': '100644 blob ff025b4744ff51aafae57848cb73e948ff8667f8',
 'Legacy.Maliev.Web/install-assets.mjs': '100644 blob 8a4f58b593d68a4d4d74ecda6685b78faf2a3804',
 'Legacy.Maliev.Web/package-lock.json': '100644 blob 8c392b351e1c1da0e11807ad7fb9ef79514d0c86',
 'Legacy.Maliev.Web/package.json': '100644 blob 66b714f086caf02cfdcc46b7ccdf9c0af5ca033a',
 'Legacy.Maliev.Web/tests/cnc-stock.test.cjs': '100644 blob d3556186984a66815a80e0c48cdc70a5622d2cac',
 'Legacy.Maliev.Web/tests/company-profile-capabilities.test.cjs': '100644 blob 8050d1ea041d9f4586686d12389b04957364c50b',
 'Legacy.Maliev.Web/tests/company-profile-presentation.test.cjs': '100644 blob 06cbdeb93f7ba70ebe31aad91f59974c12823220',
 'Legacy.Maliev.Web/tests/company-profile-workpieces.test.cjs': '100644 blob 9dec582a150fa6a4d4fcd7b5564f1b492eabca10',
 'Legacy.Maliev.Web/tests/contact-channel-analytics.test.mjs': '100644 blob c26ebffc588ffb164420c9cb9f97f653aa47beb3',
 'Legacy.Maliev.Web/tests/finish-color-matcher-core.test.cjs': '100644 blob 65f7f84de1445ec33998f7f703563515ea0d9557',
 'Legacy.Maliev.Web/tests/home-hero-callouts.test.cjs': '100644 blob 03663c20fd3d7138149208208a4bc7115f9a5919',
 'Legacy.Maliev.Web/tests/home-hero-carousel.test.cjs': '100644 blob 21fd1a01ab90530fe8c76e04d29f6503e2393c79',
 'Legacy.Maliev.Web/tests/instant-quotation-address-validation.test.mjs': '100644 blob 9e2c50dca9988620db62cee0b37ec61e17b73b6c',
 'Legacy.Maliev.Web/tests/instant-quotation-controller.test.mjs': '100644 blob b3f8c1c50a1eb4b749ac8835f6090e19d464a8fe',
 'Legacy.Maliev.Web/tests/instant-quotation-geometry-analysis.test.mjs': '100644 blob 75e5916de2f51815f55af3cac685ea909da064a7',
 'Legacy.Maliev.Web/tests/instant-quotation-gltf-units.test.mjs': '100644 blob d6850517ebd4817beb716395322d714573cafc82',
 'Legacy.Maliev.Web/tests/instant-quotation-incomplete-preview.test.mjs': '100644 blob 5a26425c2dade39887e5a7ca3fead27396e8dcb1',
 'Legacy.Maliev.Web/tests/instant-quotation-optional-fields.test.mjs': '100644 blob 3bb7f7238a90a21bd8ef93cc1846c12f7581c919',
 'Legacy.Maliev.Web/tests/instant-quotation-preliminary.test.mjs': '100644 blob bf12623d4c43d335a7348571e5e4d9a3a5e895d9',
 'Legacy.Maliev.Web/tests/instant-quotation-submission.test.mjs': '100644 blob b30aaed05cc87d5841e1a669f0dcbb934563ed13',
 'Legacy.Maliev.Web/tests/instant-quotation-three-mf-loader-patch.test.mjs': '100644 blob 8f1a24323908258e36beace89e697f1e47e49d32',
 'Legacy.Maliev.Web/tests/instant-quotation-upload-controller.test.mjs': '100644 blob a72639a327105ae3c4f2626eaf2343771f8be60b',
 'Legacy.Maliev.Web/tests/instant-quotation-viewer.test.mjs': '100644 blob e42a7099dcf21ee60eab9801097aeedd4b863d4d',
 'Legacy.Maliev.Web/tests/instant-quotation-wall-thickness.test.mjs': '100644 blob 3c62fa1c5d2e0303012a84132e50ae0d8f31b0fe',
 'Legacy.Maliev.Web/tests/instant-quotation-workflow-interop.test.mjs': '100644 blob e4a37ea4d86fbfc4b910522d971cfb486a577244',
 'Legacy.Maliev.Web/tests/legacy-ui-without-jquery.test.mjs': '100644 blob 5dc2676083a2fd76bcab35f7ad35d86b6406b1f8',
 'Legacy.Maliev.Web/tests/material-card-selector.test.mjs': '100644 blob 13e75ae2dd118c826df2053036eb63dc1ef641cc',
 'Legacy.Maliev.Web/tests/member-order-form.test.mjs': '100644 blob e471d452d28325ca5e16385923702fda46df699d',
 'Legacy.Maliev.Web/tests/release-asset-validator.test.mjs': '100644 blob 36f38da840e3eef7374e1747e610b610df5d68ac',
 'Legacy.Maliev.Web/tests/scanning-comparison.test.mjs': '100644 blob 0aab09cdbc58af3d26c54a700a58860f9ef01ba1',
 'Legacy.Maliev.Web/tests/scanning-proof-tabs.test.mjs': '100644 blob f777bd9ce4e1fa49e37df5ef23840a2813dd4528',
 'Legacy.Maliev.Web/tests/service-part-gallery.test.mjs': '100644 blob ff6341997135903f9104895b9ded5bf3dbca7661',
 'Legacy.Maliev.Web/tests/shared-public-modules.test.mjs': '100644 blob 0e4ea1eb75773f1810e83cbcbabddf65e7814200',
 'Legacy.Maliev.Web/tests/space-error-physics.test.cjs': '100644 blob 3ef76f08816c23481d29ea14e411f392818ff946',
 'Legacy.Maliev.Web/tests/webmcp.test.mjs': '100644 blob e4edc3661027795c3bc86ff87a782f45712468cb',
 'Legacy.Maliev.Web/three-mf-loader-patch.mjs': '100644 blob 2ebd55aa77254dd00404e27a078a95e2d0b0c149',
 'Legacy.Maliev.Web/verify-release-assets.mjs': '100644 blob 3b1dff5e06ad522148975cfeb2f4ec2ef57bb67a',
 'Legacy.Maliev.Web/wwwroot/apple-touch-icon.webp': '100644 blob dddfc2f42e2a324166726921b5bccfd309823f8e',
 'Legacy.Maliev.Web/wwwroot/dist/app.min.js': '100644 blob c3547cd3b7c8483189ed54ba364682ac135e86d3',
 'Legacy.Maliev.Web/wwwroot/dist/asset-manifest.json': '100644 blob 2363145c71dd34a9b20f53f9b0e536a3c0217a33',
 'Legacy.Maliev.Web/wwwroot/dist/assets/fa-brands-400-LA6RBN2H.woff2': '100644 blob e5b3ce081207a202aac7407af74696f458887c2a',
 'Legacy.Maliev.Web/wwwroot/dist/assets/fa-regular-400-NCGHE5NP.woff2': '100644 blob d470bd17584b271e1069a6435db5128a8a69f2b7',
 'Legacy.Maliev.Web/wwwroot/dist/assets/fa-solid-900-EFITDVWS.woff2': '100644 blob 3ffbb19eb1453bc16ad20e7d57febed054e30ff5',
 'Legacy.Maliev.Web/wwwroot/dist/assets/fa-v4compatibility-4GXT74DT.woff2': '100644 blob 971a2b327340c891949b29e53cb519c67173e30b',
 'Legacy.Maliev.Web/wwwroot/dist/assets/noto-sans-thai-thai-400-normal-BXXLFHTJ.woff2': '100644 blob b7386c8164a953547cb960712276918674fa219c',
 'Legacy.Maliev.Web/wwwroot/dist/assets/noto-sans-thai-thai-400-normal-HJOYPAAW.woff': '100644 blob 4933fe4bf093ae4f1defe0907372bfc7561f79dc',
 'Legacy.Maliev.Web/wwwroot/dist/assets/noto-sans-thai-thai-500-normal-DA26WOS3.woff': '100644 blob a3e79dec4e3c5c3c021b508e9c473c96a618e7dd',
 'Legacy.Maliev.Web/wwwroot/dist/assets/noto-sans-thai-thai-500-normal-NLQVN6TL.woff2': '100644 blob 09fdeab3f7f3f7d0d5ba243f1459f8b9b1df9abe',
 'Legacy.Maliev.Web/wwwroot/dist/assets/noto-sans-thai-thai-600-normal-FTSWGG55.woff': '100644 blob e61b50c40b5c06e3d12e4936f4cd7df8f7ad5a28',
 'Legacy.Maliev.Web/wwwroot/dist/assets/noto-sans-thai-thai-600-normal-Q4NFZHYH.woff2': '100644 blob 32a6baa3bd52eb6184b4f79b6f72a2e8a1c5ad79',
 'Legacy.Maliev.Web/wwwroot/dist/assets/outfit-latin-400-normal-MJX636HN.woff': '100644 blob eb2dc3005e3ddc5c4904f4fe47e044bfbbd9ccc6',
 'Legacy.Maliev.Web/wwwroot/dist/assets/outfit-latin-400-normal-WPZHQ72E.woff2': '100644 blob 2c401f59f92b53a2d43643a81a4dcb6a35c6947b',
 'Legacy.Maliev.Web/wwwroot/dist/assets/outfit-latin-500-normal-5NAIAZI7.woff': '100644 blob 4a072aa2329b707f5600098a7983c586843699ce',
 'Legacy.Maliev.Web/wwwroot/dist/assets/outfit-latin-500-normal-PXR3PSSK.woff2': '100644 blob 264ce74280afa9bce3b16dbdd77a42754ba52986',
 'Legacy.Maliev.Web/wwwroot/dist/assets/outfit-latin-600-normal-7I4OGWZG.woff2': '100644 blob aa32da2408ba303cef78ff1c3dc36cbf88983ef1',
 'Legacy.Maliev.Web/wwwroot/dist/assets/outfit-latin-600-normal-SAVBCENJ.woff': '100644 blob 87b8e187f0431a4c523be6191b66e31c50e0359b',
 'Legacy.Maliev.Web/wwwroot/dist/cnc-quotation.min.js': '100644 blob 9c83def5963192f411fd82783b5e303e4c20ce8a',
 'Legacy.Maliev.Web/wwwroot/dist/instant-quotation-viewer.mjs': '100644 blob 7ce2b5a7a78d11f61610671739c904326a04c3ac',
 'Legacy.Maliev.Web/wwwroot/dist/instant-quotation-workflow.mjs': '100644 blob 0aa205c438de80e541ecdb0be5e41573c559bd77',
 'Legacy.Maliev.Web/wwwroot/dist/route-about.css': '100644 blob 4a83c1e2e01a0969cbd89765b59650b90351cd6c',
 'Legacy.Maliev.Web/wwwroot/dist/route-about.js': '100644 blob ea21785daadb0827559d8a173c1b673e3f4db2ce',
 'Legacy.Maliev.Web/wwwroot/dist/route-error.css': '100644 blob a3f908a270bfa026757822f1a16e7b3f28700715',
 'Legacy.Maliev.Web/wwwroot/dist/route-error.js': '100644 blob 074f8749bae9309d53ac7cd7922eb1f61d7c53b8',
 'Legacy.Maliev.Web/wwwroot/dist/route-home.css': '100644 blob a6cea382dfd78cb28e67ecd0d4b926123f03d5f3',
 'Legacy.Maliev.Web/wwwroot/dist/route-home.js': '100644 blob 7ce06d8bcdba8f655d00b644231a025f0f9b8811',
 'Legacy.Maliev.Web/wwwroot/dist/route-inquiry.css': '100644 blob 345f27e33b6fa13a1d479abcfe422f6af160f712',
 'Legacy.Maliev.Web/wwwroot/dist/route-inquiry.js': '100644 blob f01fdfa1a9bd6d2ca6a8b02fcbedee169d4eedf1',
 'Legacy.Maliev.Web/wwwroot/dist/route-instant-quotation.css': '100644 blob 50c4dc1b30f53711757b83c78d52493cc3922b8c',
 'Legacy.Maliev.Web/wwwroot/dist/route-instant-quotation.js': '100644 blob 87c4ad266325551e781383f30a479430aa370c1b',
 'Legacy.Maliev.Web/wwwroot/dist/route-knowledge.js': '100644 blob c9e336063aef412a51ece6322e236747093cbb27',
 'Legacy.Maliev.Web/wwwroot/dist/route-member-order.js': '100644 blob cfaf2f9efd3f1eebe97820cfb2b92736e4609926',
 'Legacy.Maliev.Web/wwwroot/dist/route-service-cnc.js': '100644 blob 3b0439291f34abc9f1239cf572338751988c9961',
 'Legacy.Maliev.Web/wwwroot/dist/route-service-finder.js': '100644 blob e30f4dd20ad5d188c4b1c5cbd886c3d3cb0e9c9c',
 'Legacy.Maliev.Web/wwwroot/dist/route-service-finishing.js': '100644 blob 1ee8bba32d823cc36873a9a2b1c5af6699af2c48',
 'Legacy.Maliev.Web/wwwroot/dist/route-service-printing.js': '100644 blob c49827953022fdbaf068251cbf96d0fa188db2ba',
 'Legacy.Maliev.Web/wwwroot/dist/route-service-scanning.js': '100644 blob 4ced3c895ad858f741630aeae45bbea5db177c02',
 'Legacy.Maliev.Web/wwwroot/dist/route-service-toc.js': '100644 blob 3531777663d62f592f0ea396b4307875d0a1e272',
 'Legacy.Maliev.Web/wwwroot/dist/route-services-index.css': '100644 blob 043a1c007e8261b853598dd7dd2105d9fe39eb2f',
 'Legacy.Maliev.Web/wwwroot/dist/route-services.css': '100644 blob be049dc6eafe803ce0019c201f2a2a6887033faa',
 'Legacy.Maliev.Web/wwwroot/dist/site.min.css': '100644 blob 798f7d559eeb2da90c95dca929d71b3f6f680b07',
 'Legacy.Maliev.Web/wwwroot/dist/vendor.min.js': '100644 blob 13743ebb61b209f28e1c5805c9a09b33e92055b3',
 'Legacy.Maliev.Web/wwwroot/documents/arms-factory-act-2550.pdf': '100644 blob 7bb457789c59f0baa9eff46bd3a6c0d05e7d8d82',
 'Legacy.Maliev.Web/wwwroot/favicon-16x16.webp': '100644 blob 34763591a24e277c9f3d536a2c0d385fb4ac546a',
 'Legacy.Maliev.Web/wwwroot/favicon-32x32.webp': '100644 blob d8e1ea930969988b273b72071c3029e51d8fab2a',
 'Legacy.Maliev.Web/wwwroot/favicon.webp': '100644 blob 5e98d99f343c83d376f0b2ce2ff0ba8d8fb1ad5a',
 'Legacy.Maliev.Web/wwwroot/help/spacemouse.html': '100644 blob 61d86c6108a015a20326628a4ebf6ea4afc49193',
 'Legacy.Maliev.Web/wwwroot/lib/ibm-plex/IBMPlexSans-Regular-Latin1.woff2': '100644 blob a9c6407beda454c594b6f7c312e70e4c5d1ab99c',
 'Legacy.Maliev.Web/wwwroot/lib/ibm-plex/NOTO-LICENSE.txt': '100644 blob b7193a690a415988215ae1ba46b705ce2da1e3b3',
 'Legacy.Maliev.Web/wwwroot/lib/ibm-plex/NotoSansThai-Medium.woff2': '100644 blob 09fdeab3f7f3f7d0d5ba243f1459f8b9b1df9abe',
 'Legacy.Maliev.Web/wwwroot/lib/ibm-plex/NotoSansThai-Regular.woff2': '100644 blob b7386c8164a953547cb960712276918674fa219c',
 'Legacy.Maliev.Web/wwwroot/lib/ibm-plex/NotoSansThai-SemiBold.woff2': '100644 blob 32a6baa3bd52eb6184b4f79b6f72a2e8a1c5ad79',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-191da5c8b62d/.gitattributes': '100644 blob 88fae54cd4df8ab4e1ff8b596434339b7da0ec0c',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-191da5c8b62d/LICENSE.md': '100644 blob 8000a6faacf471c537530805ab29523c7732e11a',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-191da5c8b62d/LICENSE_LGPL_21.txt': '100644 blob 732811e4f1faf71870b7350f211b26e00d7bfbe9',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-191da5c8b62d/OCCT_LGPL_EXCEPTION.txt': '100644 blob 263d14ef75d9172e59213d880605729eed3041fc',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-191da5c8b62d/manifest.js': '100644 blob 7abfb39797606d3d4db4b5d9620a60ea7fb8079e',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-191da5c8b62d/occt-import-js.js': '100644 blob 5f24c7acd89aaa7400585524a89efc18c593783e',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-191da5c8b62d/occt-import-js.wasm': '100644 blob fb974f55e50ef92ff8874e12bd5c271e27b1d1cf',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-9b23eb595cd0/.gitattributes': '100644 blob 88fae54cd4df8ab4e1ff8b596434339b7da0ec0c',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-9b23eb595cd0/LICENSE.md': '100644 blob 8000a6faacf471c537530805ab29523c7732e11a',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-9b23eb595cd0/LICENSE_LGPL_21.txt': '100644 blob 732811e4f1faf71870b7350f211b26e00d7bfbe9',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-9b23eb595cd0/OCCT_LGPL_EXCEPTION.txt': '100644 blob 263d14ef75d9172e59213d880605729eed3041fc',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-9b23eb595cd0/manifest.js': '100644 blob 0fd8032eed1fa8fcffb5dd40e0e36d8333484e99',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-9b23eb595cd0/occt-import-js.js': '100644 blob 5f24c7acd89aaa7400585524a89efc18c593783e',
 'Legacy.Maliev.Web/wwwroot/lib/occt-cnc/0f4759e678ea-9b23eb595cd0/occt-import-js.wasm': '100644 blob c9905efe6d298285f5b739c04fed06d5830da6e7',
 'Legacy.Maliev.Web/wwwroot/lib/occt/occt-import-js.js': '100644 blob abec0c5eb45cc53e2bcb5a193dcab90f387de7bd',
 'Legacy.Maliev.Web/wwwroot/lib/occt/occt-import-js.wasm': '100644 blob 29458885e6b030850d352cff33ab0c22a7e36c35',
 'Legacy.Maliev.Web/wwwroot/lib/outfit/LICENSE.txt': '100644 blob c8d1ebad200111d1bcccc39703ea0c9fd8c7d5f5',
 'Legacy.Maliev.Web/wwwroot/lib/outfit/outfit-latin-400-normal.woff2': '100644 blob 2c401f59f92b53a2d43643a81a4dcb6a35c6947b',
 'Legacy.Maliev.Web/wwwroot/lib/outfit/outfit-latin-500-normal.woff2': '100644 blob 264ce74280afa9bce3b16dbdd77a42754ba52986',
 'Legacy.Maliev.Web/wwwroot/lib/outfit/outfit-latin-600-normal.woff2': '100644 blob aa32da2408ba303cef78ff1c3dc36cbf88983ef1',
 'Legacy.Maliev.Web/wwwroot/lib/outfit/outfit-latin-ext-400-normal.woff2': '100644 blob 78d36c479a5c86f9c55ba4cab21d944e307475c5',
 'Legacy.Maliev.Web/wwwroot/lib/outfit/outfit-latin-ext-500-normal.woff2': '100644 blob 4cfe8fb9ca3c41488f2d04c80830a9b3d05b3eca',
 'Legacy.Maliev.Web/wwwroot/lib/outfit/outfit-latin-ext-600-normal.woff2': '100644 blob e5634ed5029362d9f7822182c0309aa3b7869560',
 'Legacy.Maliev.Web/wwwroot/lib/three/3MFLoader.js': '100644 blob e696e55035521599ba60796bdc1f7ff0f180fec1',
 'Legacy.Maliev.Web/wwwroot/lib/three/GLTFLoader.js': '100644 blob a290b2c217690f0f9ce84a265379e894d13868da',
 'Legacy.Maliev.Web/wwwroot/lib/three/fflate.min.js': '100644 blob a0a7c5efcf4d46f2609d350bca30cc5cfa6ca48a',
 'Legacy.Maliev.Web/wwwroot/robots.txt': '100644 blob c33d48829f7c358891cf2c5c80ff8d3b6d3fdff9',
 'Legacy.Maliev.Web/wwwroot/src/app/css/about-page.css': '100644 blob 65ff3a4e159e4458e6ec03d20e81585496e576a3',
 'Legacy.Maliev.Web/wwwroot/src/app/css/app.css': '100644 blob 1050ce2794ec5a772b4dcb0157c23084230c2e0a',
 'Legacy.Maliev.Web/wwwroot/src/app/css/application-shell.css': '100644 blob 876e6fb0cb08e2f1f9f152e52e62459dfed56a62',
 'Legacy.Maliev.Web/wwwroot/src/app/css/company-profile.css': '100644 blob 3ae99db69fb793884d43e4fe1154b7d88276e12f',
 'Legacy.Maliev.Web/wwwroot/src/app/css/corporate-customers.css': '100644 blob 2d9b8612736a1b79b75b7d4f79676c8a3f1ea644',
 'Legacy.Maliev.Web/wwwroot/src/app/css/home-hero-variants.css': '100644 blob 155d492d323de3c32e3294a14fb348a8fc1b64e6',
 'Legacy.Maliev.Web/wwwroot/src/app/css/inquiry-pages.css': '100644 blob 119055796dfbcb85cf0756c2a6628d5cd1424217',
 'Legacy.Maliev.Web/wwwroot/src/app/css/instant-quotation.css': '100644 blob d0dffc71960c4c67d13e0dd03e5091568acb7183',
 'Legacy.Maliev.Web/wwwroot/src/app/css/landing.css': '100644 blob a781acdff946be7e3b942366c25dd9099dc2a76b',
 'Legacy.Maliev.Web/wwwroot/src/app/css/loader.css': '100644 blob d0fdf2f9fe5c86177d6559be913774609f33177c',
 'Legacy.Maliev.Web/wwwroot/src/app/css/motion.css': '100644 blob 8cc1849fecd1c6614b8613d4106fefbf336ab337',
 'Legacy.Maliev.Web/wwwroot/src/app/css/public-footer.css': '100644 blob b95f866bde9fb32d9ede17c7f1328c443652822b',
 'Legacy.Maliev.Web/wwwroot/src/app/css/public-navigation.css': '100644 blob f4ecfee09d8a71903d53658ba9ef980d957837ac',
 'Legacy.Maliev.Web/wwwroot/src/app/css/scanning-comparison.css': '100644 blob 71d0ef84765ddac68eb634c369e7793aabf5f19b',
 'Legacy.Maliev.Web/wwwroot/src/app/css/service-finder.css': '100644 blob a4e6edece2ad5436abf4cb927e80aa72ad5b2712',
 'Legacy.Maliev.Web/wwwroot/src/app/css/service-pages.css': '100644 blob dc6ef939009517355d0593e50fc9e09a30964983',
 'Legacy.Maliev.Web/wwwroot/src/app/css/sidebar.css': '100644 blob 0752bcb2b0cf83d5543bf6a13cfa800acc59dd4f',
 'Legacy.Maliev.Web/wwwroot/src/app/css/space-error.css': '100644 blob 752bafaec33b11562fd8a11a710624c9698a65dd',
 'Legacy.Maliev.Web/wwwroot/src/app/css/sprite.css': '100644 blob 792e434e58e2e13b0ac3d461564235d844bf3d80',
 'Legacy.Maliev.Web/wwwroot/src/app/css/stepper.css': '100644 blob 6a9994d72b45f661c432a7cfb9881a3cb07a099b',
 'Legacy.Maliev.Web/wwwroot/src/app/css/table.css': '100644 blob 54151e633a7177709dca526de60fc7d18545f87a',
 'Legacy.Maliev.Web/wwwroot/src/app/css/thai-lookup.css': '100644 blob 3440151fc37494368bd2540e9d3f08716da330c3',
 'Legacy.Maliev.Web/wwwroot/src/app/css/toc.css': '100644 blob b393bc7ec6dd2b0f8e0219fc95a9149c9190d6a3',
 'Legacy.Maliev.Web/wwwroot/src/app/css/typography.css': '100644 blob 83d9ab360122a6732126dbd0c7c2dd9cebddf6a5',
 'Legacy.Maliev.Web/wwwroot/src/app/js/additive-quotation/additive-simulation.worker.js': '100644 blob cfaf6edaf66dab27b98783f3ca8f4382e3037c70',
 'Legacy.Maliev.Web/wwwroot/src/app/js/app.js': '100644 blob cd644fb7d20a5cc8b775a885c46cebf0a56212cb',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-ball-rest.worker.js': '100644 blob 720a628c2c3c855fb762feb9736697c05c421028',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-cad-document.js': '100644 blob b428da7aa4d8cdd6e10ff0124b5c2dda4894b11b',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-cad-surfaces.worker.js': '100644 blob f78d7dee0f3ac17f630b4c542c33ef834e230f1f',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-engine.js': '100644 blob 85240c0936ffed296b8a8efdb4e34fe3907fcd5e',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-feature-graph.worker.js': '100644 blob e56916e5be437420b236ef53533ecff52a7fb349',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-feature-recognition-prismatic.js': '100644 blob 4a4985e3e3017a56607b1968dbef4766200fbb46',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-feature-recognition-thread.js': '100644 blob 5b5533131e0907489c43d214d97c6e181eebd193',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-fixture-catalog.js': '100644 blob ca23ebaa6fdf5dec2d074d8b735450ff303adc6a',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-fixture-clearance.js': '100644 blob fb052e4b6ec10064ef85f185dbb4eef73e6d2d3f',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-geometry.worker.js': '100644 blob ca9deee9192a094253e2c3a539bbdbbcff6bc3b7',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-machine-capability.js': '100644 blob ce2a6cc50078faba282cda566c518e17ace49088',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-manufacturing-evidence.worker.js': '100644 blob 3cad1aa4acfb3e7c053ae89cf936e28a8c2e3fff',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-material-catalog.js': '100644 blob e74461e1aa4fbbcb1697fc6e8bf5e6d91ed0e9dc',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-native-dispatch.js': '100644 blob 0805cc5ff05322f061bf62ef74c8d75e29f92254',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-native-regions.js': '100644 blob c245b6e4a182e5706b8f368d7744ecd2e8f9c054',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-native-thread-evidence.js': '100644 blob e3d7529f1563a0d521d7a68e99433127e1a20f56',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-native-topology.worker.js': '100644 blob f3f7000d8dbf1d372662b56896a1550a19fc6c98',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-plan-contracts.js': '100644 blob 3c92b72025e16a4ba577a87723664f7466c76c3e',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-plan-validator.worker.js': '100644 blob d417c4f36bc3e26f8d7cbbb373756d0045fccfb3',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-planning.js': '100644 blob 07216dea763e9b32da1bf4df38cd30c23bc34d49',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-process-compiler.js': '100644 blob 012ba9f08df9a632834058961fd6e50516dedeb3',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-quotation-config.js': '100644 blob 857b7e459cd79eec1c0375bc2f5f367a333fadb2',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-quotation.worker.js': '100644 blob c9ece8f199a5d556e71b54221b1a0adec2da21e1',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-reach.js': '100644 blob dfc69078387eef8c4fe4b48e372c9cc125dc1c36',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-setup-planner.js': '100644 blob a04b4716626678edd1bf731b606ed7e5f2260bf3',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-spatial-field.worker.js': '100644 blob 55ff815130e80e44e99ae622f07a062a8acd480f',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-stock.js': '100644 blob 2799f4fff0e1288cc4bfcfa4dd9f929231345c0a',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-tool-library.js': '100644 blob 241e6cbe050b8bfc520298169f8e583e9c177142',
 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-topology.worker.js': '100644 blob fbba684374c681aa845e523ab641abbc0cfd8707',
 'Legacy.Maliev.Web/wwwroot/src/app/js/company-profile-capabilities.js': '100644 blob 3ebb347158444de71a5e41ae9fd619cf8ad07fc3',
 'Legacy.Maliev.Web/wwwroot/src/app/js/company-profile-presentation.js': '100644 blob 0ee3da7ceed4577c537beeba1880e3c41800db9f',
 'Legacy.Maliev.Web/wwwroot/src/app/js/company-profile-workpieces.js': '100644 blob d223a23f9e06f452bf5ff433ab5017b52e00288c',
 'Legacy.Maliev.Web/wwwroot/src/app/js/finish-color-matcher-core.js': '100644 blob 7d2acff803339d94130ec7b9b7565ab39f63cb57',
 'Legacy.Maliev.Web/wwwroot/src/app/js/finish-color-matcher-preview.js': '100644 blob 610490f05e1bf2946c5a2a00708d3b0cba5f87e7',
 'Legacy.Maliev.Web/wwwroot/src/app/js/finish-color-matcher.js': '100644 blob fca2b6d19fe38bd29478f6eef710fe6e851f06ff',
 'Legacy.Maliev.Web/wwwroot/src/app/js/hlc-colour-atlas-data.js': '100644 blob fd5676c7e5f9160c809f81f2795f5bf351bb3597',
 'Legacy.Maliev.Web/wwwroot/src/app/js/home-hero-callouts.js': '100644 blob 95269b1414e2fcd887162839a9ad7053f681c4e0',
 'Legacy.Maliev.Web/wwwroot/src/app/js/home-hero-carousel.js': '100644 blob a6a98a56221601bb3899b640457fb1daee46e471',
 'Legacy.Maliev.Web/wwwroot/src/app/js/inquiry-pages.js': '100644 blob 5c00ad52c8ba93f2f0ea9af71bff8f8f97f37db4',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation-address-validation.mjs': '100644 blob e84cddc5dc31970d59ef91f37c9df2577f485544',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation-controller.mjs': '100644 blob f226aed043391d1eeaeca96ba4ac70abd17c3b3d',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation-optional-fields.mjs': '100644 blob ee1d4965841d07cbb0ef779a339b7924f939e86c',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation-preliminary.js': '100644 blob ddb890e27fa02f15b080ed19d639fe293e43246c',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation-submission.mjs': '100644 blob 17e79932225b6feb13dd001b3a20f201c7317089',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation.js': '100644 blob 092d86c377c18f4abcc00284e1c79ecb8993fbd5',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/empty-scene.mjs': '100644 blob 5dcec7d8345b8dab4b2e433dadd7900bbb282fb2',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/geometry-analysis.mjs': '100644 blob 95cb1d07f352ceae440a464033687e59d353befe',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/material-card-selector.mjs': '100644 blob 9545abe2de8541effab2073fa0fc931b4dbceaf4',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/model-viewer.mjs': '100644 blob aa9c47a203faa02f51986a77c8ff7e345115469e',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/upload-controller.mjs': '100644 blob 518ac4543f6ddaae8771abe62b0be6d5e523400f',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/wall-thickness-presentation.js': '100644 blob 2b4bfb09a6371b46038f0963561df6bc26f20b19',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/wall-thickness-runner.worker.js': '100644 blob 93a5f0f6a9e5df7dc9d894b6b0ddd28e351672e4',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/wall-thickness.mjs': '100644 blob 91c3ca0fa0860a743d9dcfa51d08d35959b482a6',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/wall-thickness.worker.js': '100644 blob 513f180e2b4b81b04273916ae337ca6278b57cda',
 'Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/workflow-interop.mjs': '100644 blob fbfeba20426c5500d001c98efb9d40775792cf19',
 'Legacy.Maliev.Web/wwwroot/src/app/js/knowledge-navigation.js': '100644 blob d46bf53b3e30b186dab15d4b3c1c21c278cbda8e',
 'Legacy.Maliev.Web/wwwroot/src/app/js/knowledge-search.js': '100644 blob 4e02cc1a82a760e7a793374cca4f0d2f93a7f700',
 'Legacy.Maliev.Web/wwwroot/src/app/js/lookups/thai-lookup.js': '100644 blob 125946a6a5d19fefd613de3ee68d5706da4b0fa3',
 'Legacy.Maliev.Web/wwwroot/src/app/js/material-comparison.js': '100644 blob 60bf6915791d7589c0d2f090e22d60f5f9961890',
 'Legacy.Maliev.Web/wwwroot/src/app/js/member-order-form.js': '100644 blob 76be9b416ccbe055321a871af83dd97fc7c8047a',
 'Legacy.Maliev.Web/wwwroot/src/app/js/model-viewer/model-viewer.js': '100644 blob fc94005d5c0d99da5a7b3ded7642b5dfc4c03158',
 'Legacy.Maliev.Web/wwwroot/src/app/js/model-viewer/model-viewer.worker.js': '100644 blob 92659b3d5d4a0ae35a970c0fe91d2afe8f96cbf8',
 'Legacy.Maliev.Web/wwwroot/src/app/js/model-viewer/spacemouse-bootstrap.module.js': '100644 blob a96cf4270fcc47c23c212a6d13c7329c9b3d99a6',
 'Legacy.Maliev.Web/wwwroot/src/app/js/model-viewer/spacemouse-navigation.js': '100644 blob d00fb12c3039659dd94c7e54cca6f290bcb35c90',
 'Legacy.Maliev.Web/wwwroot/src/app/js/motion.js': '100644 blob 2c8b50ed72f2dd40343cda153108ff646e598eef',
 'Legacy.Maliev.Web/wwwroot/src/app/js/scanning-comparison.js': '100644 blob c4fa1fcde19983f04bcb38e8d4c084e1cc0e792a',
 'Legacy.Maliev.Web/wwwroot/src/app/js/scanning-proof-tabs.js': '100644 blob 5ad918f77024e38e4480af6b0da5dbff9df120d9',
 'Legacy.Maliev.Web/wwwroot/src/app/js/scanning-workflow.js': '100644 blob f3d6c68d8480a24114e3bf5cf74cabdef54334f7',
 'Legacy.Maliev.Web/wwwroot/src/app/js/service-finder.js': '100644 blob e3e8f03eb4e722e1a87e130fe6870fe80a2cf209',
 'Legacy.Maliev.Web/wwwroot/src/app/js/service-part-gallery.js': '100644 blob 41902ebe2e4805a3f51040c799e276567d4d40b0',
 'Legacy.Maliev.Web/wwwroot/src/app/js/service-toc.js': '100644 blob 8f11c16c4bb06d874612c196fb4e2c2b816cb83f',
 'Legacy.Maliev.Web/wwwroot/src/app/js/sidebar.js': '100644 blob b4aec720badfd7bf28ba73d3fa5770cdc5521f54',
 'Legacy.Maliev.Web/wwwroot/src/app/js/space-error-physics.js': '100644 blob 135a31de0267a42b8f7eba16978f77634e5851d7',
 'Legacy.Maliev.Web/wwwroot/src/app/js/space-error.js': '100644 blob 0e1a19591a7916af64877d8f2087172c5573e7e6',
 'Legacy.Maliev.Web/wwwroot/src/app/js/webmcp.js': '100644 blob 3de0c1369187bfdf2b4cf2f41182bce9a3024094',
 'Legacy.Maliev.Web/wwwroot/src/data/printing-material-details.json': '100644 blob eb6cb27b6db365efc0af6289f181fa15a268bac5',
 'Legacy.Maliev.Web/wwwroot/src/images/304-cnc-machining.webp': '100644 blob b126e0822fe8c3dbef4f38cdd50d656461c26a52',
 'Legacy.Maliev.Web/wwwroot/src/images/3d-canvas-placeholder.svg': '100644 blob 7b926d98bb79dae8a8e5ad3a45c205070a973cd4',
 'Legacy.Maliev.Web/wwwroot/src/images/3d-printing-cover-expanded-1.webp': '100644 blob a72e9fb7ce448a3316b414b265850c81657458c9',
 'Legacy.Maliev.Web/wwwroot/src/images/3d-printing-cover-expanded-2.webp': '100644 blob 22e38881b3bdd4ebeba4e40b115a50417acae67f',
 'Legacy.Maliev.Web/wwwroot/src/images/3d-printing-cover-expanded-3.webp': '100644 blob ba7faeddd17332a983af6254ed161a046fecd5d3',
 'Legacy.Maliev.Web/wwwroot/src/images/3d-scanner-banner-1.webp': '100644 blob be24d3523ee297ec8852bef15172e3022face8ed',
 'Legacy.Maliev.Web/wwwroot/src/images/3d-scanning-cover-expanded-1.webp': '100644 blob 6933a0a1fb876cb95c26ac85f814304e223feed5',
 'Legacy.Maliev.Web/wwwroot/src/images/3d-scanning-cover-expanded-2.webp': '100644 blob 22c47d8b6cb4990d6714e89c62c39ce61deb9184',
 'Legacy.Maliev.Web/wwwroot/src/images/404.svg': '100644 blob f80b9013a161b73c3b57d14ac7221365c90b7f3a',
 'Legacy.Maliev.Web/wwwroot/src/images/5052-cnc-machining.webp': '100644 blob b3ac22561262f94bf211f344c21850155d1a046e',
 'Legacy.Maliev.Web/wwwroot/src/images/6061-cnc-machining.webp': '100644 blob 77b16204c5a88fc3582c150a9df753e3bf7a8875',
 'Legacy.Maliev.Web/wwwroot/src/images/7075-cnc-machining.webp': '100644 blob cca9caf476ab31fa9e72c80521f90261714d0aa8',
 'Legacy.Maliev.Web/wwwroot/src/images/Flags/sprite-flags.webp': '100644 blob 979a0833e41ba0e478c914a1048891e1c556cff6',
 'Legacy.Maliev.Web/wwwroot/src/images/Flags/thailand.webp': '100644 blob 2171c939202fa5bb1477e458acfbaa8b0d78b1fd',
 'Legacy.Maliev.Web/wwwroot/src/images/Flags/united-states.webp': '100644 blob 1af0cb2d2a23cce05dd1d17d3af2e7d9689a43e7',
 'Legacy.Maliev.Web/wwwroot/src/images/Knowledges/ANSI Features/Concentricity.webp': '100644 blob 3c3edba18c98acffe425192a06ef2f868de01c2e',
 'Legacy.Maliev.Web/wwwroot/src/images/Knowledges/ANSI Features/Diameter.webp': '100644 blob 3b8314cf3279d08979e97edfecc1bfc19acc07e1',
 'Legacy.Maliev.Web/wwwroot/src/images/Knowledges/ANSI Features/Parallelism.webp': '100644 blob b04d3e32913f0821e36c525e1122a7338abad5ad',
 'Legacy.Maliev.Web/wwwroot/src/images/Knowledges/ANSI Features/Position.webp': '100644 blob 1f37f3bdc5c2536d7f2d85b8e7f33576cabe12a1',
 'Legacy.Maliev.Web/wwwroot/src/images/Knowledges/ANSI Features/Profile.webp': '100644 blob c3cce3a85913ba152e51d9384b31e64e8cebd8f5',
 'Legacy.Maliev.Web/wwwroot/src/images/Knowledges/ANSI Features/Roundness.webp': '100644 blob bab18ed4f7588226716b7d39d0f29eccd0aa9f89',
 'Legacy.Maliev.Web/wwwroot/src/images/Knowledges/ANSI Features/Runout.webp': '100644 blob 196ff7867d29f86c1e3ea9a6a8adf5f0443837f0',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/logo-bangkokbank.webp': '100644 blob 3312de0fb09b4bd20a66ccd577c04e6c184ab0e2',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/logo-cash.webp': '100644 blob 6b3caf7577b0e51a2ee042e6289a9f90dbd96889',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/logo-kasikornbank.webp': '100644 blob 4ebbafffc0962377bc7522349f5d11913ed2175f',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/logo-krungsribank.webp': '100644 blob 5c3271536ff3b7b6b806cc652be6e64db0f2604c',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/logo-krungthaibank.webp': '100644 blob eb2b6957a77e75f75801d921164c0a31bc738d46',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/logo-mastercard.webp': '100644 blob 4732b5bdf323854d4ff4cb4160aed460b07a1f97',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/logo-paypal.webp': '100644 blob b459af726079736af085b9e70d41dca2e909b334',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/logo-scbbank.webp': '100644 blob f016c32589e6d90046e6b8362b253f985dcd5764',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/logo-thanachartbank.webp': '100644 blob 04146545f19bd7810de52cf56b267a098e44b0db',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/logo-tmbbank.webp': '100644 blob b1b25a6899d517daf2aa2162417f86bf4d54c107',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/logo-visa.webp': '100644 blob a9f80abea3de44d732f5b9d9ec2ebeb4e096ff25',
 'Legacy.Maliev.Web/wwwroot/src/images/Payments/sprite-payments.webp': '100644 blob 518c9bf6db8e4b4dc01031dc907c3ea77834ffc2',
 'Legacy.Maliev.Web/wwwroot/src/images/ShippingCouriers/logo-dhl.webp': '100644 blob 26f9dc967529eb549eb2457f3490eae16f2cdb08',
 'Legacy.Maliev.Web/wwwroot/src/images/ShippingCouriers/logo-dhlexpress.webp': '100644 blob 5318f125e0b015d8ee0f61254401b713f99911f9',
 'Legacy.Maliev.Web/wwwroot/src/images/ShippingCouriers/logo-grabexpress.webp': '100644 blob 2fb46fc0eca9540c00e32f648dffa116eb377830',
 'Legacy.Maliev.Web/wwwroot/src/images/ShippingCouriers/logo-kerryexpress.webp': '100644 blob 190c3cd1e67d034dfd7932f473e6a1e49075b733',
 'Legacy.Maliev.Web/wwwroot/src/images/ShippingCouriers/logo-lalamove.webp': '100644 blob 70379efad61530c59a9547a2af0faa3ff273a944',
 'Legacy.Maliev.Web/wwwroot/src/images/ShippingCouriers/logo-lineman.webp': '100644 blob 4b391ced1b25ac048e4d199a2b249b1e67e330bc',
 'Legacy.Maliev.Web/wwwroot/src/images/ShippingCouriers/logo-thailandpost.webp': '100644 blob c49f84887413a5e2fe32565b197c9fa0cd7fe040',
 'Legacy.Maliev.Web/wwwroot/src/images/ShippingCouriers/logo-ups.webp': '100644 blob cf15171eeb4b47d5be579c0212936d5f8d9cab54',
 'Legacy.Maliev.Web/wwwroot/src/images/ShippingCouriers/sprite-shippingcouriers.webp': '100644 blob 00ab06b1860f9170cde2c3ee720c3af29b1bb674',
 'Legacy.Maliev.Web/wwwroot/src/images/abs-3d-printing.webp': '100644 blob 1c7c6b6259ef4f6de59356a9739e37272ddaad97',
 'Legacy.Maliev.Web/wwwroot/src/images/account-forget-password.webp': '100644 blob 16b39319bd728f00cf447ab8e76b79f7c18abe69',
 'Legacy.Maliev.Web/wwwroot/src/images/account-signin.webp': '100644 blob 5515b239941a3e494345c5d406ecb5baa61ef711',
 'Legacy.Maliev.Web/wwwroot/src/images/account-signup.webp': '100644 blob ea739385f72672283b5f60e384cf9e2c002ff101',
 'Legacy.Maliev.Web/wwwroot/src/images/asa-3d-printing.webp': '100644 blob 65965cede3ffbebb49c750bcea74e0225463d3f4',
 'Legacy.Maliev.Web/wwwroot/src/images/box-dimensions-annotation.webp': '100644 blob 31da0a62876dafba38b994bfdc249d021cfae074',
 'Legacy.Maliev.Web/wwwroot/src/images/brass-cnc-machining.webp': '100644 blob 550e124c1a67e5a3a0dcb59ee4cfbfb1c51ff6ab',
 'Legacy.Maliev.Web/wwwroot/src/images/cad.svg': '100644 blob b6286d73de8a5cbf6e7eff1e6362aea244a56c5c',
 'Legacy.Maliev.Web/wwwroot/src/images/career-3dprinting.webp': '100644 blob bc35b971f730bd909fd79a3660c370ca24194065',
 'Legacy.Maliev.Web/wwwroot/src/images/career-heading.webp': '100644 blob 04e25b23555ed164dbff299bc3454d48d5617bfa',
 'Legacy.Maliev.Web/wwwroot/src/images/cnc-machine.svg': '100644 blob b9268cde69570d37d16f0bb72ac5b27ea0c7782c',
 'Legacy.Maliev.Web/wwwroot/src/images/cnc-machining-cover-expanded-1.webp': '100644 blob 5607cc54bc569a7095778e4d459c5768eef1aca4',
 'Legacy.Maliev.Web/wwwroot/src/images/cnc-machining-cover-expanded-2.webp': '100644 blob 30204d7a0b464bca64c9d5de54b79e962b8127f1',
 'Legacy.Maliev.Web/wwwroot/src/images/cnc-machining-cover-expanded-3.webp': '100644 blob 6df29b8fb84a454e0ae8d07e0284b32cbeed064f',
 'Legacy.Maliev.Web/wwwroot/src/images/copper-cnc-machining.webp': '100644 blob 34d4bb0bd77c33ea36c5719f9cec82348e2806f8',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/aeon-fantasy.svg': '100644 blob 1628fa693987cd9ca3e65662b8191cc3acb1c6d2',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/air-liquide.svg': '100644 blob 906c9f8fc9fca956aa167d05804fe00a30db02c9',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/alten-korea.webp': '100644 blob 28297bbc07b01e43d0690b32470a53a1be5558f0',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/bangchak.svg': '100644 blob 286fa352aedffec2f6b8260c0b49d31c99b26e16',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/bem.svg': '100644 blob 14b084f1ee496d84dd9af132e944b5720474f33d',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/crown-ceramics.webp': '100644 blob 7a6ad870de653f8fbfbe136d0d552bb05ab3127c',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/crown-seal.png': '100644 blob bd72b4b00d8d65848241919f517eb692eaa886f4',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/crown-seal.webp': '100644 blob 26b4c4477dded4c04c4811791c2bcaed4f6d941f',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/do-day-dream.webp': '100644 blob 512d4c2ca25fa3c56290150de952b9394876763c',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/fabrinet.png': '100644 blob 1aed2161ba8f692ef2a523adb12f96b71e84fc69',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/fabrinet.webp': '100644 blob de1bfebfb46ca7bb85b4d3c8a62e4bb89e951c1c',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/forth.webp': '100644 blob 60bae6060c853877ebcb3da2b6c92267db1bfb3a',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/grohe.svg': '100644 blob 9372c470e37afe833e04033c7312a699e29e2793',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/khon-kaen-sugar.png': '100644 blob 63e658b3739da162d41c61ff16ba7f8a5b3aea65',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/khon-kaen-sugar.webp': '100644 blob f4f861508f5f99eb31969f38e337fceb3236c869',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/kobelco.gif': '100644 blob 9bc957bdd8b1d79ad91487b560f76f18c2ebd85a',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/kobelco.webp': '100644 blob 9f71c8d99ce0311b0d53c9e6fb00e52fb4cba80a',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/kohler.png': '100644 blob 1d6dd48ac11b710ec0f219bb2c87089a69987f03',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/kohler.webp': '100644 blob 40de74184958806a9af20d5599a4d51aeb108de3',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/kumwell.webp': '100644 blob 741bb11fa620f8a1972dacc75e4b1f28d0f82025',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/lenso.svg': '100644 blob 76008173e1ea56c14548635c0241b0d4346abfe9',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/lixil.svg': '100644 blob 723059aff9c8a67a58ef5935e70bc22013bb5df4',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/mektec.png': '100644 blob 4d6ddca173368afa88b563226c43c4939c4fff7d',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/mektec.webp': '100644 blob 28305bc8c1df96e5aacff28c9ab834623b46814b',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/mfec.png': '100644 blob 531c23eb7188f44b6c0ce94da12207c271f3bd0a',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/mfec.webp': '100644 blob bed7e13d4cd0dd383999740f75de5b5d35ef2a53',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/minor-food.png': '100644 blob 69544724a554792184b5f39d4835a6e702d4ce0c',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/minor-food.webp': '100644 blob 7443b9f820c18a44ddb4df8b179e6a8ddcca45de',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/mitsubishi-materials.png': '100644 blob 70266937619a2e576a31788d07de41987746012f',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/mitsubishi-materials.webp': '100644 blob c011d36e41ad336ad57a4824f4b6b141983ce447',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/murata.svg': '100644 blob 80f04b8c96c1c642c584e4670ba9ab21d8268e40',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/olympus.png': '100644 blob 7e58f285ae59caac385d907beff3bfa4e0f61f58',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/olympus.webp': '100644 blob 8fffe8ad769a2f46e5bd8e92ce6aec054150f1aa',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/ptt.png': '100644 blob a2a15c062fd2570efff342b5003a11e4af2d3841',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/ptt.webp': '100644 blob a838c40f8b8fc92c10ce0f5d2a35c4492db50fb3',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/pttep.svg': '100644 blob 37aefad58b38854b70339fe94faf9a4c906faeea',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/sahamitr-pressure-container.webp': '100644 blob b046223b022e04a56fb70a0bae6163cede5d7353',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/siam-motors.svg': '100644 blob af72d16b7e1608d7de6d95420578b83d23951748',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/siemens-mobility.svg': '100644 blob c07e468152f10f3aae247c3817ac8bbe92c71bf7',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/singha-kameda-current.png': '100644 blob 4b72a122cab586f746c110ebad99f8d24acc26de',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/singha-kameda-current.webp': '100644 blob cf15f10ab13b3398b676a8aea1385a21ace61e09',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/sp-syndicate.jpg': '100644 blob 94019818b17505ddfa0b54c58d0ea34d4b7e2fbf',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/sp-syndicate.webp': '100644 blob 0dedc39890c6c37bdcde3944066d0fcaafb4c27d',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/thai-agri-foods.webp': '100644 blob 7636c77aca15f9e5a9844b41249c632de6502fac',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/thai-asahi-denso-en.png': '100644 blob e4110763d9a1e99b7d0cc58c4d11fd89c4646f78',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/thai-asahi-denso-en.webp': '100644 blob c3d0dd52c10f1c4e70a8fd8325bf9eb560d2688c',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/thai-coconut.jpg': '100644 blob 221d22c268dd45d3841b81d6b546d28e824aa802',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/thai-coconut.webp': '100644 blob e0240debc4aa555ee8add9afb57c999ad509933f',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/thai-optical.png': '100644 blob 86b20b55e4533cf5f3a1ed9ee29dbbc414711d30',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/thai-optical.webp': '100644 blob 4845345dc82394658378c39b2772ee7e9e3ccbb3',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/tpi-polene-power-emblem.png': '100644 blob 6d25ffb99bd420f594f5b091b9817e0789a7f879',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/tpi-polene-power-emblem.webp': '100644 blob 272237cff5036229afc32576c26cd6cd7109a125',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/yg-1-official-mark.png': '100644 blob 2f84b3389a5333b2227e655b24ff4f5e52bd074f',
 'Legacy.Maliev.Web/wwwroot/src/images/customers/yg-1-official-mark.webp': '100644 blob fd6dcafcde3253d4e60d008fcd9076de1e0b438d',
 'Legacy.Maliev.Web/wwwroot/src/images/delivery.svg': '100644 blob 8b209e41dcba26fc87d3659984c7c9db0bb36c0f',
 'Legacy.Maliev.Web/wwwroot/src/images/error.svg': '100644 blob 6f6ffb0d5615e1d9b3ff399936bd936f77579d3d',
 'Legacy.Maliev.Web/wwwroot/src/images/error/astronaut.webp': '100644 blob 49ca36fd60fb1c0fb652dac85ed36739aabb7947',
 'Legacy.Maliev.Web/wwwroot/src/images/error/earth-horizon-mobile.webp': '100644 blob 20c320fd8711211682e98931b750c41d87560bbb',
 'Legacy.Maliev.Web/wwwroot/src/images/error/earth-horizon.webp': '100644 blob b69c10abcaabdac767deea539e24e80a6708ff2f',
 'Legacy.Maliev.Web/wwwroot/src/images/f-ogo_RGB_HEX-58.webp': '100644 blob 658537a991b494a68273bbbb00c0050764aa23ff',
 'Legacy.Maliev.Web/wwwroot/src/images/fdm-printing.webp': '100644 blob cd73a75170b9b4c68a79d7238e84bf50008dc7f9',
 'Legacy.Maliev.Web/wwwroot/src/images/footer-background.svg': '100644 blob 917fc84431e09a084eaa413bd9f03264a7c1b6d3',
 'Legacy.Maliev.Web/wwwroot/src/images/hips-3d-printing.webp': '100644 blob 4b19ad7e8a3873679f1a7e376c4b8b67d092730e',
 'Legacy.Maliev.Web/wwwroot/src/images/homepage-cover-1.webp': '100644 blob 4b38904d2eb8bfcb8ef8866ad60a53d4b4b7058e',
 'Legacy.Maliev.Web/wwwroot/src/images/inexpensive-rp-hero-background.webp': '100644 blob a2674bfaef594c5b5b7f7163465bab9b17f9aa9c',
 'Legacy.Maliev.Web/wwwroot/src/images/instagram-logo.webp': '100644 blob ff39620017c747d3592fb02cc315b458b81712b0',
 'Legacy.Maliev.Web/wwwroot/src/images/instant-pricing-feedback-hero-straight.svg': '100644 blob 3d1e8817fb97fbea5741fa2f6abc4aea9deed732',
 'Legacy.Maliev.Web/wwwroot/src/images/instant-pricing-feedback-hero.svg': '100644 blob 404b0a56d4adc748a9430921cb9e1bfd1073518c',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/anodize-dimensional-growth.svg': '100644 blob 190a6756bdae3ea76d0117eb36f401361826a408',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/cnc-corner-clearance.svg': '100644 blob 2234ca7b5497fd584ea73bcea836ba2b73ab1b45',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/cnc-wall-stiffness.svg': '100644 blob f22982c9b91ecc5c711528ebe51529dc7e5440e2',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/diagrams/process-air-bending-v3-small.webp': '100644 blob 03780d544f142bab1bf874f26253acbad50146cf',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/diagrams/process-air-bending-v3.webp': '100644 blob f2745e40f83fc2f78d9a7a7b294cb8bcc4674ca0',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/diagrams/process-fdm-v3-small.webp': '100644 blob 3236aaf1c24b49ee94ac5b69966be5503bbd8870',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/diagrams/process-fdm-v3.webp': '100644 blob 88a82e1235d02a751673356299e1d99398ab308e',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/diagrams/process-milling-v3-small.webp': '100644 blob bbb7b2982e306c119202915c8aefbbc1b60b70f5',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/diagrams/process-milling-v3.webp': '100644 blob 816b62e9755077c6a8e991d290560d3c7796a2cd',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/chatter-small.webp': '100644 blob bd8e3effb27290b5103f0c5efed9665cd984b46e',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/chatter.webp': '100644 blob 66f847c61544eb01cf6c1068735994ff2bfd59c9',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/cnc-overview-small.webp': '100644 blob 49a863c6abe53a9cb31cc9fdd0f74754d2b895b6',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/cnc-overview.webp': '100644 blob 2cf4e9d5b90bd70d468482fc3c404b726d54c98d',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/cutting-tools-small.webp': '100644 blob cf8cadda595d1d5a9a712fa7e0f7da60a9a7aab6',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/cutting-tools.webp': '100644 blob dc5ea85de92301eadee8b03b3937ab81f888a165',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/dimensional-inspection-small.webp': '100644 blob 0298b0482052ad9e5d88fdf72ef183bfad18af9a',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/dimensional-inspection.webp': '100644 blob 018c11396b8316f3d7de6d8c9fc3c845540185e3',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/engineering-plastics-small.webp': '100644 blob 34150108666546d4f48f41cf6e0a24b62bc9db63',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/engineering-plastics.webp': '100644 blob 76dae672b01ca888da2d6b04964d541b3902eba4',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/engineering-workflow-small.webp': '100644 blob 3f76dec13d38296b4334a67771ddbab52e7f4ab7',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/engineering-workflow.webp': '100644 blob 54c97fe9ce334fa68406218277f8def74d0e77e5',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/feeds-speeds-small.webp': '100644 blob 1a940bb85534549a81cdf2bc01a0aa532af7f821',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/feeds-speeds.webp': '100644 blob c5554058c8e197775466c28935ca7a755eaec461',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/heat-treatment-small.webp': '100644 blob 1b1c4e98d9ee9367f0d03f5bc7119862343daa11',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/heat-treatment.webp': '100644 blob 96668ab1fda0850391e0e607c3bba4d2693fa7fe',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/holes-tolerances-small.webp': '100644 blob e4ffb773c6fe377e4af402ab66e1e8b0bb9c9848',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/holes-tolerances.webp': '100644 blob 4a48ee8cc096cbc12eaab60d4c577557ffdef44d',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/library-small.webp': '100644 blob 00eb843e3d8e3ab750e25e8e0aee66af738a3770',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/library.webp': '100644 blob e0419ad1b748534d7fd62fe317218af365eac3f3',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/material-selection-small.webp': '100644 blob ecf8f2b7ddc39077edddc440360d6993fe099707',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/material-selection.webp': '100644 blob dc61cd0e69aaa9e84c000bb8c6d04e380c487a5a',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/metals-small.webp': '100644 blob c5a7633348dbb313c054f8f857484845db117e52',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/metals.webp': '100644 blob 6bebf53b05c99228683760bd458ad670e275fac9',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/multi-axis-small.webp': '100644 blob 6cd0bbc8d7513a07b7b3894da1e0f2feb75ee40c',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/multi-axis.webp': '100644 blob 9eeddeb3fb172532e697d83ef282c18e0b0d72ab',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/printing-small.webp': '100644 blob 757122343ffc52c4b9aa5763fd36a2344d9ad563',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/printing.webp': '100644 blob 051d6e00ab3a11e9fb3133b69d1e1f07b638bc88',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/process-air-bending-v2-small.webp': '100644 blob 0390d76d289086002011ea3c17318bbae9f4a118',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/process-air-bending-v2.webp': '100644 blob 407e49f1bb15c2c7a56dfe597086c1a85876a18e',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/process-fdm-v2-small.webp': '100644 blob bbd1924ea3233b345f5e62710747daf0668ef1ef',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/process-fdm-v2.webp': '100644 blob 40edf15d12e971f9b59647ad73ce496fec367741',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/process-milling-v2-small.webp': '100644 blob a5009177c9a971eb88cb1db74f7ef467fe31d17f',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/process-milling-v2.webp': '100644 blob 20fe39f64b4eb6622efc77f83c2ac430e851f406',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/process-selection-small.webp': '100644 blob 6fc9e23b69a566f4d50cf6d73ed083243dbd0b6a',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/process-selection.webp': '100644 blob 623bb42f005866788e454767fdfb57b406186325',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/quotation-files-small.webp': '100644 blob 5d15ff2391cd3a513e9e9c62f63bcd9d7fb621ea',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/quotation-files.webp': '100644 blob 7aa3a64d15095cce97b2fe4207f0e58cb39f5e26',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/scanning-small.webp': '100644 blob 9f2acd15f01c829e08af54d9157e2b2b62cae4d4',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/scanning.webp': '100644 blob 39feb4239a039fe5bd13ed9f496bdeda18659e46',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/service-guides-small.webp': '100644 blob d12a43a9549a4eb9734f9035eaab3aaa9956cf57',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/service-guides.webp': '100644 blob e2eac24c4dcb28b1b9bda4c3b014fdbfd5f8f13d',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/surface-finishes-small.webp': '100644 blob 996dd603c133c64d6ea0c291d59fa5624db9d7f5',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/surface-finishes.webp': '100644 blob 0c55ece68f358c77f57b4b8ca7d89258d57bdfab',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/warpage-small.webp': '100644 blob f8c2271c14cca635f5864129bcb404bb6d97eb91',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/warpage.webp': '100644 blob ed5e23ca9b8d5f44db6205a5123835553af7431c',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/workholding-small.webp': '100644 blob b53fa04ea8e5273a1aaf9685c69e8ccab15cf83d',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/workholding.webp': '100644 blob 59cd08027593a020498fd667ef7a25620b91a0bf',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-cnc-preparation.svg': '100644 blob d9fee4db56638dc242419f9c4dcb81b0f391b734',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-cutting-tool-reach.svg': '100644 blob 23046a19cff12c1674892c4063146a4319a5e1e5',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-feeds-speeds.svg': '100644 blob 41ccf1e9f5417e511535eb96200406932136df31',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-hole-depths.svg': '100644 blob 164d84c3ac22ccd0e45c3a1128a526f8e9973709',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-machining-access.svg': '100644 blob 4da62a319cb71b2702909f046b14391f3db2222f',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-material-criteria.svg': '100644 blob 292081cd3525db7ed62779483b0b3f9ac39f0efb',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-metal-designation.svg': '100644 blob d7c3f33ed118f69353205495c3988cbdd5cc171e',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-polymer-creep.svg': '100644 blob bf7318e313cdfb0d7774b96ce6a9a6d04deb0c5c',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-print-orientation.svg': '100644 blob 488a03af97f7d2f09607b09c135f7297710442cf',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-process-routes.svg': '100644 blob f68e77865a172928c1741a64ba4c1d2b4155aede',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-quotation-package.svg': '100644 blob 65e8bd788a08096ed0c47cffa316b7621daac877',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-review-loop.svg': '100644 blob f9c28311541b4a4251c7a0d6b8aa0a8555874e73',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-scan-to-cad.svg': '100644 blob 9f7f00c287d6e1e0f82a30ba481cefc919159339',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-service-inputs.svg': '100644 blob 4a9275a0e0588269d964db9a30303ce24b97d48c',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-thermal-measurement.svg': '100644 blob 3352a9cde32d439b013691b55545d40559b0666b',
 'Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-treatment-allowance.svg': '100644 blob a49380b786525a7c84713b8eb2994418809953fc',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/about-customer-partnership.webp': '100644 blob 319b8565392b3420ee44b64a9fd60fd21d193fa1',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/camera-bracket-cad-v5.webp': '100644 blob 0d4f2807b9bbe5ff348473cbbe4a75486bf2312e',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/camera-bracket-deployment.webp': '100644 blob 00f3fb90b23c3d448f6588b9f5b40b0c8ba3c201',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/camera-bracket-production-v5.webp': '100644 blob 1857b2f909ca085e451c701c6c6e8406c2349843',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/camera-bracket-prototypes-v4.webp': '100644 blob a88e2c0b3ea0074f2ae1c540e4f3b7026e50eb26',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/camera-bracket-sketch-v6.webp': '100644 blob 73056abd985e57b91e624be97f3c66511bb7b21f',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-cnc-finished-bracket.webp': '100644 blob 710449855cb6f1a6c3d46313bf192950559e4597',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-cnc-finished-housing-v2.webp': '100644 blob d12e92416d8b750ae0ace51b9061b36a40ee9009',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-cnc-fusion-cam.webp': '100644 blob 04317e89e577cc122371a4908bb81dd6f294ab87',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-cnc-vf2-machining.webp': '100644 blob 2ea7500a2818fbcec45950d351184c86c0a8fe6c',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-design-mouse-cad.webp': '100644 blob d042da37d4e2d6d568e9aa9e66d6510c5a14806c',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-design-mouse-revisions.webp': '100644 blob 62838be9900159c8a2a9e51e9a297a8bf6a30661',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-design-mouse-sketch.webp': '100644 blob 630944c202aca5685661ef4dd7051e04f30346ce',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-injection-batch.webp': '100644 blob 10fdb5f459ed8a5d857ce6e094ca4093b66530eb',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-injection-concept.webp': '100644 blob bb5e25823d1fd3b77e05c6d412f5406af57860eb',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-scan-cad-grey-v2.webp': '100644 blob 3dac4d3af1cb1634def97c60670afa43167933f1',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-scan-capture.webp': '100644 blob 8f21abd2839fb2fd9098cd0c37346cdafce6be93',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-silicone-concept.webp': '100644 blob 6ff2efbf7f3f12669636a0f417dd12a55ca8c820',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/capability-silicone-pour.webp': '100644 blob 67130dcfa1509236141c0284f1266c863352f540',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/home-enclosure-cad.webp': '100644 blob cc32a604d1c1522cf53e055b317c66220213d34d',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/home-enclosure-inspection.webp': '100644 blob 0fb3c88dc02707f30af6060808cb856fdc283cbf',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/home-enclosure-prototypes.webp': '100644 blob f608582a7a0da67d9f38beea6101561c933e6b52',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/home-enclosure-sketch.webp': '100644 blob eb8108139e3074fe10768cb078de82eb4bbf5d72',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/landing-cta-part.webp': '100644 blob 24c6a2030bace5c4e8406d01ec0182c20c5bfbd5',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/landing-hero-cnc.webp': '100644 blob 47347cdb15815bdd1137fd13ae2c3d53a0a2313f',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/landing-hero-part.webp': '100644 blob 2283a8474de41f97d8fc6c165535072da0c9bfdc',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/landing-hero-printing.webp': '100644 blob 0e436e0692116a93ef88dd46fc4d861f20793929',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/landing-hero-scanning.webp': '100644 blob 68f51ea01d6caae3d03222d4ea6e827a0183e8c5',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/landing-service-cnc.webp': '100644 blob 6b1e2b1e39fd3843abf8bd1cdea64eb6439bcec4',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/landing-service-printing.webp': '100644 blob 566a288e026106e1f67e2117e86a3e4955cfe52c',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/landing-service-scanning.webp': '100644 blob f4ede19e4462911bf67da427776bad4de98defb9',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/profile-hybrid-gripper-cutout.webp': '100644 blob f1c3e10743fb90b4532b5a81f76e83f5c29681a7',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/reverse-engineering-worn-part.webp': '100644 blob 9c6a793678745434cbef0d57ceefee3630fb5841',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/reverse-workpiece-adapter.webp': '100644 blob b20111e8ddfd6f8afd885686f4f69cea577796a8',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/reverse-workpiece-machinery.webp': '100644 blob a0b627027a179f0e32cd19ab12e16bcd3053ae70',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/reverse-workpiece-sculpture.webp': '100644 blob 58b9080625f3ca75735a28538e2d5b45c3f91dc7',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/service-directory-finder.webp': '100644 blob 74b8924d42bf37d443821c9c3966e7b9a2dcb2b2',
 'Legacy.Maliev.Web/wwwroot/src/images/landing/silicone-owl-sculpture-casting.webp': '100644 blob f381c702f8c7daf1cac334dc1df7b4b1d1bd9d24',
 'Legacy.Maliev.Web/wwwroot/src/images/line-logo.webp': '100644 blob bf9852d7a96975d17af8662eacdbe0245e08473f',
 'Legacy.Maliev.Web/wwwroot/src/images/line-qr-banner.svg': '100644 blob b7a699df5e2cf8753c8e7b10394b61d0a15cdb33',
 'Legacy.Maliev.Web/wwwroot/src/images/line-qr-clean.svg': '100644 blob 3ee6187147f11c4d002d724b5eb3eebd165d170d',
 'Legacy.Maliev.Web/wwwroot/src/images/line-qr-clean.webp': '100644 blob 9eee18731e25d63a899e9bf2047ba4e2263a1678',
 'Legacy.Maliev.Web/wwwroot/src/images/line-qr.webp': '100644 blob a94e60523fbc4251bcdbca3fc716c5cf82e73dfe',
 'Legacy.Maliev.Web/wwwroot/src/images/manufacturing-cost-english.svg': '100644 blob 7483e32dfec77eb1b25f7499c986bddc90535802',
 'Legacy.Maliev.Web/wwwroot/src/images/manufacturing-cost-thai.svg': '100644 blob 911712093568f2424f56500e754d4fff4577c784',
 'Legacy.Maliev.Web/wwwroot/src/images/mjf-printing.webp': '100644 blob ee569cb3d7baac3121831d323576e591075053a4',
 'Legacy.Maliev.Web/wwwroot/src/images/multicolor-3d-printing.webp': '100644 blob 49c1034a0c04f9d458ab4a364c2126721532ea6f',
 'Legacy.Maliev.Web/wwwroot/src/images/natthakarn-vanasrivilai-profile.webp': '100644 blob 3abd4e6b85ad56f8f13f4d5e3284419f55be5049',
 'Legacy.Maliev.Web/wwwroot/src/images/navbar_logo_black.webp': '100644 blob c79b016287bb99bc0daf2f8247d0218d597f0578',
 'Legacy.Maliev.Web/wwwroot/src/images/navbar_logo_white.webp': '100644 blob 172717b9726e226ea0aaf2baeeb453d4071eafb7',
 'Legacy.Maliev.Web/wwwroot/src/images/opengraph/og-image-1.webp': '100644 blob 677f649e700b150713521c538198ea91ca69c129',
 'Legacy.Maliev.Web/wwwroot/src/images/opengraph/og-image-2.webp': '100644 blob a751a7eb5184e77790f74664e19eedd24c266176',
 'Legacy.Maliev.Web/wwwroot/src/images/opengraph/og-image-3.webp': '100644 blob 60bcbc1451f0655f5686de15f82e4f842706e2bc',
 'Legacy.Maliev.Web/wwwroot/src/images/opengraph/og-image-4.webp': '100644 blob c5692fc746026b428b633d6fbd9e8c5369e67932',
 'Legacy.Maliev.Web/wwwroot/src/images/opengraph/og-image-5.webp': '100644 blob 8ecbda0fc2da696d961a2e7e81676699107c32f4',
 'Legacy.Maliev.Web/wwwroot/src/images/opengraph/og-image.webp': '100644 blob 9e05d60e81640ac9bf4446d9738f5ec2422ed383',
 'Legacy.Maliev.Web/wwwroot/src/images/pa12-3d-printing.webp': '100644 blob 68fcb1de45106de9ca7904a7258c377fb2482d2f',
 'Legacy.Maliev.Web/wwwroot/src/images/pa6-3d-printing.webp': '100644 blob 62fb48fc786903a4c6b26fe2c2286f7f0953c646',
 'Legacy.Maliev.Web/wwwroot/src/images/pacf-3d-printing.webp': '100644 blob 0a553d2365c78190a8d6c32757f35a9fe1da9239',
 'Legacy.Maliev.Web/wwwroot/src/images/pc-3d-printing.webp': '100644 blob 5f4ba0c78827ab9502dda8d057a4cc32d06be22b',
 'Legacy.Maliev.Web/wwwroot/src/images/petg-3d-printing.webp': '100644 blob 39da52ce8392242656f8389c722d79490537a379',
 'Legacy.Maliev.Web/wwwroot/src/images/pla-3d-printing.webp': '100644 blob fabfb23ea9a0d95ca7d94d083416f03b9afd277d',
 'Legacy.Maliev.Web/wwwroot/src/images/pva-3d-printing.webp': '100644 blob 6e226f188c6bfee85cd1659a09c9fb0b8b74ea11',
 'Legacy.Maliev.Web/wwwroot/src/images/quotation.svg': '100644 blob 5f2e0ade99347b99c6e0ed2a7b1780bf4d316ea5',
 'Legacy.Maliev.Web/wwwroot/src/images/resin-3d-printing.webp': '100644 blob ab1ed80d8a03db8b951205bd1d929c6c7da3db22',
 'Legacy.Maliev.Web/wwwroot/src/images/s50c-cnc-machining.webp': '100644 blob 08f72b515782188610a2ad23a589a7f3befa96d9',
 'Legacy.Maliev.Web/wwwroot/src/images/scm440-cnc-machining.webp': '100644 blob b82dbcc2a66604e3ba57f97ed9ded471ecb12a55',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/cards/cnc-card-functional-prototype.webp': '100644 blob 01b8261e626910c30f7a450f5bc75bcb89a7e14c',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/cards/cnc-card-jigs-fixtures.webp': '100644 blob 02f69576fdc1e03e832af18d4dfe97bd4366e455',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/cards/cnc-card-milled-parts.webp': '100644 blob 24cea1bcaaeeac52db255a27f287a93d3ebc1f5a',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/cards/cnc-card-repeat-production.webp': '100644 blob af9223b575e6d77d898648c6f71424b33da8796b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/cards/cnc-card-replacement-parts.webp': '100644 blob 9700a4f9d2c4cea4dc3cf1c885792461ff90e436',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/cards/cnc-card-turned-parts.webp': '100644 blob 56fb6488b90eae9b39eb031c1fa1b839f18ec190',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/cnc-hero-640.webp': '100644 blob 4be4e425b5d13d8e0db3f5b05a2eea1ec8b32ed6',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/cnc-hero-768.webp': '100644 blob 061ce46b323db392898f2e051cfd12d8dfe0e018',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/cnc-hero-960.webp': '100644 blob 49797686fdd81eaf162e0ddc159045be59e49f5e',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/cnc-hero.webp': '100644 blob de885701a94648024fa569ca988c5884aba87de5',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/cnc-materials.webp': '100644 blob c6502ca2a9647563644a0964507fd835770ef3bc',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-angular-micro-part-1024.webp': '100644 blob '
                                                                                                  'd2301c54c12e0342d767b17b4224a1af79cb1cc0',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-angular-micro-part-640.webp': '100644 blob '
                                                                                                 '98a8fba9c62ea2bd628f72783b184037c13673c6',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-angular-micro-part.webp': '100644 blob d2301c54c12e0342d767b17b4224a1af79cb1cc0',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-anodized-handle-scale-1024.webp': '100644 blob '
                                                                                                     '1df4c74dcd23903d3f1e88fb70adc4d652636969',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-anodized-handle-scale-640.webp': '100644 blob '
                                                                                                    'eaca90090a5301bb9d31057b84f0823a4af137f0',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-anodized-handle-scale.webp': '100644 blob '
                                                                                                'ed9d67b47f75eac6d16c51d2bc75335ec59d93f1',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-arched-linkage-1024.webp': '100644 blob 9080e0bb099ea267df200b896a74549194e608c8',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-arched-linkage-640.webp': '100644 blob f3df7d37377304072e8db61842a9529d28e4d977',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-arched-linkage.webp': '100644 blob ab5a1fd03c09ffba7ee12c9c4e70f8fab6f61196',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-brass-relief-1024.webp': '100644 blob 0dbebfcbb8bfb66ffcc7e3edb7f9f008431444e6',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-brass-relief-640.webp': '100644 blob 9ed0a763d0e4cfdb913a3a8bfbb82babc5d97eb5',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-brass-relief.webp': '100644 blob 0dbebfcbb8bfb66ffcc7e3edb7f9f008431444e6',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-controller-faceplate-1024.webp': '100644 blob '
                                                                                                    'a85ab93fb0bbc4334e761a8e26efe67b0bd1b86c',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-controller-faceplate-640.webp': '100644 blob '
                                                                                                   'ffbcd4421e22ac8df5838483e14537e7af4604b9',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-controller-faceplate.webp': '100644 blob 9bcfaf863c6e3894ad9a89055886e70e366d0d3f',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-cube-billet-1024.webp': '100644 blob da40337feeafda5604ae104ea2c4065349d29b20',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-cube-billet-640.webp': '100644 blob 2e1a84b7cf7e5355323ed2897c784485130a780e',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-cube-billet.webp': '100644 blob 0fe70e7c7cc89a1d4363371ce71ec4418c98a0fe',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-die-blocks-1024.webp': '100644 blob c4dc066252778c36609d27e52b174eb3ffc1dbea',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-die-blocks-640.webp': '100644 blob 0f690025a27dd859a88ceb97088af05ae1f236ac',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-die-blocks.webp': '100644 blob aff293cc47e0e0dab3552ff51e16c5a70ce0f1d1',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-folding-handle-1024.webp': '100644 blob 276eda45c0b7cc30c7aad5855e41419b57e0ccc2',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-folding-handle-640.webp': '100644 blob 9449a99aa28895817cedde9b7b9a749d9cc59fc5',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-folding-handle.webp': '100644 blob 7f27ad264c30fb972962ae7c9f0d748f745b7516',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-machined-disc-1024.webp': '100644 blob a6db85d1222b7a60eba4f5169d68a8713f503829',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-machined-disc-640.webp': '100644 blob 80e374e757efeb1f0f65a4ebae22718839c5e838',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-machined-disc.webp': '100644 blob e80bf4c737fae1ee657e0aa7656ee9a3e103f38c',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-mold-blocks-1024.webp': '100644 blob d86649ce231cae4838745310060495209dbfc995',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-mold-blocks-640.webp': '100644 blob 24faf10aedba2dd758ca131a64b2b44c8787c4c4',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-mold-blocks.webp': '100644 blob 2974de48edf26c218f2ed4cab0a5e7f9ad9a9ab8',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-oval-cover-1024.webp': '100644 blob cb78c06a2a5797c2626d6156f09d9721d1113437',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-oval-cover-640.webp': '100644 blob 7f4d2bb2773436e6c48fc6c9779ffa3e2785d61f',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-oval-cover.webp': '100644 blob cb78c06a2a5797c2626d6156f09d9721d1113437',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-pocketed-plate-1024.webp': '100644 blob b4d43a2e6c6d037e7e652239cc7936595f45f979',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-pocketed-plate-640.webp': '100644 blob fc2df03d2470a9bc5b2301930c66f68273d91d3d',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-pocketed-plate.webp': '100644 blob 5d5d95429467fdf917fbad1aa6b8f3537a06abd0',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-relief-block-1024.webp': '100644 blob 599d6887ffb9120cd7c31119ef2505f7e5333960',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-relief-block-640.webp': '100644 blob 699849721841d8bf1c5df22b02360534d09c8747',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-relief-block.webp': '100644 blob 140b9474b0ca9de8febb591922c7ee0afc308d08',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-slotted-rail-block-1024.webp': '100644 blob '
                                                                                                  'a9c59b0530a7df5818240f8fbe61d2bee3d28f07',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-slotted-rail-block-640.webp': '100644 blob '
                                                                                                 'd58bf057e6f25e58c3be3b7fcec3c4a3c1ce66df',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-slotted-rail-block.webp': '100644 blob 58da66a2145f31165ee50e7a830be0bebb8a446b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-sprocket-plate-1024.webp': '100644 blob 78020d9c4306b0d17ac1f01b74b27095becf9692',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-sprocket-plate-640.webp': '100644 blob 6b21a5886633dc23d0fd1d6426781b319e93cf4b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-sprocket-plate.webp': '100644 blob 47ed333e8de4ae0c97e508be129c12e01acd3481',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-stainless-component-1024.webp': '100644 blob '
                                                                                                   'e2eb0807bf41e0a99ed2634e558c736b549ffee5',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-stainless-component-640.webp': '100644 blob '
                                                                                                  '465f9cc6a855bbb2ac768406b7e29b219104e857',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-stainless-component.webp': '100644 blob e35548a7d38182c2fa3314867f4f6560e1867ad9',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-turned-shaft-1024.webp': '100644 blob a2ed54a38d334eec137650a66842808ce4c7d69a',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-turned-shaft-640.webp': '100644 blob 0a6394c1e9a2c458c240764afdff66ea5183043b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-turned-shaft.webp': '100644 blob 3f731194d454ac82faebd74cbd57bc3748285beb',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-upright-bracket-1024.webp': '100644 blob f54f6d52f88a1e5a2bc94d6b5ca428dae8cd1d19',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-upright-bracket-640.webp': '100644 blob 2320217296fa51c5f13854ef6a311374385d2598',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-upright-bracket.webp': '100644 blob f54f6d52f88a1e5a2bc94d6b5ca428dae8cd1d19',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-upright-housing-1024.webp': '100644 blob 1f6423c5c8b81e044732fc73707ae3d06677f123',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-upright-housing-640.webp': '100644 blob 6181cacc3ace33adeef0ebdd9ee2f2038a3b1750',
 'Legacy.Maliev.Web/wwwroot/src/images/services/cnc/part-bento-cnc-upright-housing.webp': '100644 blob 7afa4d077285b88cb1e4519d061607b1f02ed91c',
 'Legacy.Maliev.Web/wwwroot/src/images/services/custom-manufacturing/custom-manufacturing-story-640.webp': '100644 blob '
                                                                                                           '1d75ee7004dd065f19f49f0feee567d778470f45',
 'Legacy.Maliev.Web/wwwroot/src/images/services/custom-manufacturing/custom-manufacturing-story-768.webp': '100644 blob '
                                                                                                           '62356a88eddb4fb8f6ce25ca59d0e7608264204d',
 'Legacy.Maliev.Web/wwwroot/src/images/services/custom-manufacturing/custom-manufacturing-story-960.webp': '100644 blob '
                                                                                                           '1719135ef208f6e2307d0d5f3dac1a6ca4a2d414',
 'Legacy.Maliev.Web/wwwroot/src/images/services/custom-manufacturing/custom-manufacturing-story.webp': '100644 blob '
                                                                                                       'f697a1e782bcd9767c395ec51bc76cf6282ce717',
 'Legacy.Maliev.Web/wwwroot/src/images/services/design/design-dfm-review.webp': '100644 blob 2aefea9aaa7f137e92f80e496a7e7d8217421d1b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/design/design-inputs.webp': '100644 blob 60d246d23b854f92b1f13e535ce7ae0a0a711f51',
 'Legacy.Maliev.Web/wwwroot/src/images/services/design/design-workflow-640.webp': '100644 blob 0d14ef9178469bb44adc5b92a380c2656ae54486',
 'Legacy.Maliev.Web/wwwroot/src/images/services/design/design-workflow-768.webp': '100644 blob 53e4e1ac37068cff6d447778bdcd5f19ea7240b0',
 'Legacy.Maliev.Web/wwwroot/src/images/services/design/design-workflow-960.webp': '100644 blob b7a2b93830f71d1d4432718b815c1f4cabb17440',
 'Legacy.Maliev.Web/wwwroot/src/images/services/design/design-workflow.webp': '100644 blob 6ae08f112804994be4d57d4e925db4fbc29b18b9',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/injection-card-workshop.webp': '100644 blob '
                                                                                                 '294a009aeac6198ceec8e90bcf5f72a72b1a5752',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/injection-service-hero-wide-640.webp': '100644 blob '
                                                                                                         'ff94bef80bb66f53f2c57516e42aea3403b62eed',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/injection-service-hero-wide-768.webp': '100644 blob '
                                                                                                         'dd7557cecf623f1e951482f6a3c297bc1042bde2',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/injection-service-hero-wide-960.webp': '100644 blob '
                                                                                                         '6f29b3da862c27cdd2d29f965a7e645fb86d066f',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/injection-service-hero-wide.webp': '100644 blob '
                                                                                                     '38bcd29c287d600b6dbeccd2ebe26c23c382104b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/injection-service-hero.webp': '100644 blob '
                                                                                                '917bf4b69f619ee53b2d9a2e70a4e2e239d7eae5',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/part-bento-pp-source-derived-1024.webp': '100644 blob '
                                                                                                           'd09b938fbe3f9c4f80f2a2330440810c411a3433',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/part-bento-pp-source-derived-640.webp': '100644 blob '
                                                                                                          'cc104b9b4d260b8fff2d08e26afb7c88242ed69f',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/part-bento-pp-source-derived.webp': '100644 blob '
                                                                                                      'bd666f48060acc250cd488444b1fccc38c52f316',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/pimm-50g-controller.webp': '100644 blob ba706c01b17ac595cf7e835cc740d862aceb55f2',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/pimm-50g-cover.webp': '100644 blob 6126ae5607908ec2891267748263f9a686c36105',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/pimm-50g-hero.webp': '100644 blob 87858375ec9ab266a8fd11d3d515b1af45209933',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/pimm-50g-nozzle.webp': '100644 blob 1e83cb118c07e92da8dd582b224639df1b0549cc',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/pimm-50g-square.webp': '100644 blob 3ea386afe675a5ff7b64034063fb6318a8890fdb',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/pimm-sample-mold.webp': '100644 blob 51b6cf572995ec1ddb26b0423a14b5c8da199f1d',
 'Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/pp-injection-molded-component.webp': '100644 blob '
                                                                                                       'daa3c433e309c998f30ad6ef7544df42ea47a8e8',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-abs-esd.webp': '100644 blob 7eb8c8142e78a09caf704726b1ba84ab8ab7decd',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-abs-fr.webp': '100644 blob d3f118cbe22089ebf6de0e745cb77d8e2c179abb',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-abs.webp': '100644 blob 7eb8c8142e78a09caf704726b1ba84ab8ab7decd',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-asa-cf.webp': '100644 blob 6c014de8b63e2cf8fbfcfd07d15bf87419b3c3d2',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-asa.webp': '100644 blob 96812f83e43cd1a4ea2badb729c4ee5d130a79dc',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-castwax.webp': '100644 blob 83c9131bb13d98c4a4cf5aba3d49d67f7f380377',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-f80.webp': '100644 blob b611b6e1b0d256a50d4ad4337081a67103114a66',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-g217.webp': '100644 blob f9620149a89206761c05d59b48f8ceca144ef7bf',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-hips.webp': '100644 blob 55f19e27923adeb52ebf1f3d8a1e90ebbb204c91',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-k.webp': '100644 blob 46ef3f795bcef4cce27de521c71e4ee543e285ac',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-m68.webp': '100644 blob c9059d434ac9bc00241071ca345761b3c15d0a83',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-pa-cf.webp': '100644 blob 38dfac269405c3eb62ed996998b464e48f9ce08e',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-pa12.webp': '100644 blob c672fc5c0981d7c6dbb6c3f8082d4768cd6ddf24',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-pa6.webp': '100644 blob 7c9dc2b4a394a2c383a767e1e52934c727be7fbb',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-pa612-esd.webp': '100644 blob beceb1ad5cc632e59e5137da4e1dbec335d0f18b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-pc-esd.webp': '100644 blob beceb1ad5cc632e59e5137da4e1dbec335d0f18b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-pc-fr.webp': '100644 blob cfe54a425e25742878b711bbe8bd25a4d68a0e94',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-pc.webp': '100644 blob 1aae576008eb65d18fdd344c0ff82e59c34d9236',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-pet-cf.webp': '100644 blob 7ae60e64d99ac371ff8258886b23613cd6e1edde',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-petg-cf.webp': '100644 blob c8d17b37068fb04f08a5b0d597add252ace63d9e',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-petg-esd.webp': '100644 blob b724eb04009c1dd88def17cb5bb00e2ae8418024',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-petg.webp': '100644 blob e29978de4804999e86f843bf9ffa449801dc9c99',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-pla-cf.webp': '100644 blob 48d8c25005f79135e280176f57b2fc4702ba76e2',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-pla.webp': '100644 blob 8c14673e671e433618ba57ddf3eac0995dfef8cf',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-pva.webp': '100644 blob 3d51fbb0e2df47f17591d8b7b693ee4cf494f453',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/materials/material-tpu.webp': '100644 blob 6d7c62c38b0581a46b6ec62a9ee8e1770a7780bd',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-abs-fan-1024.webp': '100644 blob e33bf7b5caab6912911208c8d3060a3904cab51b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-abs-fan-640.webp': '100644 blob 32e2db665024ab1adfc75c10dee6185708d3c849',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-abs-fan.webp': '100644 blob 7bcf8f0a2e2cae0d92ff40c4f863845543781edb',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-abs-hips-lattice-cube-1024.webp': '100644 blob '
                                                                                                      '230e1a95e451ee60ffdbffb81d7097c3becb7aaf',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-abs-hips-lattice-cube-640.webp': '100644 blob '
                                                                                                     '7bcb5c9410a18bb1443bcdc12e635b4e1b159849',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-abs-hips-lattice-cube.webp': '100644 blob '
                                                                                                 '26c8329715eb24eb356de262dafde25841e67891',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-asa-bracket-1024.webp': '100644 blob 0af9369a8700932bc956f8d9021d30d076a42136',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-asa-bracket-640.webp': '100644 blob 37ce1da601ed125498ba826cb54745e4d7f11a59',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-asa-bracket.webp': '100644 blob d56361d8bdb9e634e976d781c776f03d7b28068f',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-bluecast-lattice-ring-1024.webp': '100644 blob '
                                                                                                      '1990aea9f5a3933bae0fd05037fb94c011ff6814',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-bluecast-lattice-ring-640.webp': '100644 blob '
                                                                                                     'dbbce5099ec9ec3d08482820bb61b5b2ca94766c',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-bluecast-lattice-ring.webp': '100644 blob '
                                                                                                 '314b1d27c59db2f9775d4bbc2ea36406bad02c78',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-castable-wax-dragon-1024.webp': '100644 blob '
                                                                                                    '01b32c112e79959a144d5e71be8340fa50cf8fbc',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-castable-wax-dragon-640.webp': '100644 blob '
                                                                                                   '328e4bc684d22fa37ef6fbe46fe38de376d23c36',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-castable-wax-dragon.webp': '100644 blob 394ffcb70a7040d7a6cbe96f6b2564cafb7c25bc',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-hips-flanged-part-1024.webp': '100644 blob '
                                                                                                  '2d489d0bcb12e1c116ca6f1088977e5456c66d48',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-hips-flanged-part-640.webp': '100644 blob '
                                                                                                 'a604fe73248006ec07557b3ab5c434c6a59ad1cd',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-hips-flanged-part.webp': '100644 blob 526ade7355175600de2120787915b589dcaf4055',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-mjf-pa12-linkage-1024.webp': '100644 blob '
                                                                                                 '1217fde93c0363c696f066dbe41eba07d39b99f8',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-mjf-pa12-linkage-640.webp': '100644 blob '
                                                                                                '2c511cbcc5a302b5fa8072363dab69b563090b8e',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-mjf-pa12-linkage.webp': '100644 blob 17da67232a579ad8e5eba01bd516a5ccd81533b0',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pa-cf-wheel-1024.webp': '100644 blob d2b46f0c800e44a2eb20e2d11ddcbdca5f02b391',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pa-cf-wheel-640.webp': '100644 blob 09a61d8e8092146700eb0496c108fda16420efba',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pa-cf-wheel.webp': '100644 blob 0097a02839ab27fad7d4f08f548f0f631b31a740',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pa6-detail-1024.webp': '100644 blob 150889a8eaacaf78e8963c41bd1ccb4af87579f5',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pa6-detail-640.webp': '100644 blob c361a10b43b05e183c1e816967ad5cfc96a3b88f',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pa6-detail.webp': '100644 blob 00323c130692beb9580c8d434f7e4b4444355abc',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pc-esd-threaded-component-1024.webp': '100644 blob '
                                                                                                          '4bcb329e98c4d2bdad39d57d392731cc107c3da9',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pc-esd-threaded-component-640.webp': '100644 blob '
                                                                                                         'cdf8aa56a983c444af64ddbcd540f196143d5664',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pc-esd-threaded-component.webp': '100644 blob '
                                                                                                     'bc97423567280d9379d1798dab5ff78ec10dd0a9',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-petg-cat-1024.webp': '100644 blob 8a57bf8e4ee8bbab42e7591f3ff7bc465350c767',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-petg-cat-640.webp': '100644 blob 807abb14d039db5b564be26869178532f319d9c3',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-petg-cat.webp': '100644 blob f80cb2d55cde8be53b36b48bddff6fb21d1c4ecd',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-petg-shell-1024.webp': '100644 blob a67d1f8a7cf48f97c700dd9ee4d4e9079e2e06c1',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-petg-shell-640.webp': '100644 blob 951c6c3ab28099459723886fd0cfd5037329cb82',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-petg-shell.webp': '100644 blob 6c93cdd134d15df1d7082e6777b9d2cd943083c7',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pla-cf-bracket-1024.webp': '100644 blob e8c8d946c54c25344a54169e8af9f2c42b42c334',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pla-cf-bracket-640.webp': '100644 blob b6669187500d92a453a44d5048c82cd41159dd9c',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pla-cf-bracket.webp': '100644 blob 743d74a6a5abc3b713509a38557fb4d0f486e85a',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pla-character-1024.webp': '100644 blob 7642d0d58b148b61e32a02e44f44c728e95a6fdf',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pla-character-640.webp': '100644 blob 297d061f6557f1c08a393c533979355b3ad97814',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-pla-character.webp': '100644 blob 542fa6bbdae8d6abdea5c9f4026e4c6c49ca31df',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-resin-component-1024.webp': '100644 blob '
                                                                                                '4fcff64aabece26d0b91bf4f3773c6dd45718165',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-resin-component-640.webp': '100644 blob 19116445ec12089f89e551abe58ce68b0034f80d',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-resin-component.webp': '100644 blob 6b84f6f10c1cb7fa66f0a275ec45c7bc9ceb3817',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-resin-frame-1024.webp': '100644 blob cf61cef9e7f38a87842e3f5f7e722226075784df',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-resin-frame-640.webp': '100644 blob 7421c581bdf5b6bf222990b2043d534acbba54bb',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-resin-frame.webp': '100644 blob 31c02fb16cb7d7c99e747149d14684f8b0e3de08',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-rubber-resin-pulley-1024.webp': '100644 blob '
                                                                                                    'a975b2b117e111e0aadfddf47cde394bb73f15c4',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-rubber-resin-pulley-640.webp': '100644 blob '
                                                                                                   'e2d8939c4a2c702563fdc98f33ab4cb90434c9a7',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-rubber-resin-pulley.webp': '100644 blob 7b1278f8376aa43dbcded645f3fd7beab87a9692',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-sls-pa11-ai-review-1024.webp': '100644 blob '
                                                                                                   '5bd607db1b4d310868854af88a8837d7960f4c1c',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-sls-pa11-ai-review-640.webp': '100644 blob '
                                                                                                  'b92968af358e05a74927f5f631e819ab27eadd72',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-sls-pa11-ai-review.webp': '100644 blob c03d0ebc0a8a46ac3d967cdbd9e46cde779b97aa',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-sls-pa11-impeller-detail-1024.webp': '100644 blob '
                                                                                                         'c2b5bf01ba2d507b78d65e17f679f3e12d700e20',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-sls-pa11-impeller-detail-640.webp': '100644 blob '
                                                                                                        '5dda76749ad2a22b0cb78dc7f9a1bead28d31d2d',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-sls-pa11-impeller-detail.webp': '100644 blob '
                                                                                                    '42bf5834884ae940b0dfc452ea38608b6e20830a',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-sls-pa11-impeller-wide-1024.webp': '100644 blob '
                                                                                                       '9a09c89692e2c816bef5ce89b2226e3740ebb064',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-sls-pa11-impeller-wide-640.webp': '100644 blob '
                                                                                                      'b6326b6255dc91b78902f85203ac30619ca7d028',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-sls-pa11-impeller-wide.webp': '100644 blob '
                                                                                                  'ef11bb9dfb0a10cd7fcb729db3cfcc16282441bf',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-tpu-handle-1024.webp': '100644 blob e2001e95191522d21715bc3c1435f1dd6d323038',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-tpu-handle-640.webp': '100644 blob 33e885b82bc9e7356a07970af445f7f1dd1c4451',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-tpu-handle.webp': '100644 blob 6d85c5b237dfc8ba5628930b78c3cb792bc673ca',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-tpu-sandal-1024.webp': '100644 blob 5e1d99c66e9ab6ef83e77cd74bea1144b3fc23f3',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-tpu-sandal-640.webp': '100644 blob 0c55c1236854a743b1cf622dd96fcb9472d0107d',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/part-bento-tpu-sandal.webp': '100644 blob 4a3b60b791a787ec9965ec6c1ed4d38d1d598396',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-fdm-application-640.webp': '100644 blob 4aee90e832eaefc98f1664ad4df5b13b3a435f3b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-fdm-application-960.webp': '100644 blob 6b423be15a459188108934a24febe7ba4230981a',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-fdm-application.webp': '100644 blob 87b9ac05f5c33e31bd00ff9fe0a1c45f9de9f050',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-file-manifold-check.webp': '100644 blob 6a351ec9d6adc05e149d6b91043599f8b975e67e',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-file-orientation-support.webp': '100644 blob '
                                                                                                  'd0888926c5a53b6c7d7b182e76ac25159dce4716',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-file-wall-clearance.webp': '100644 blob c2f7f93fe8a6db4b859dc752a089a05859bc4ef9',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-finish-clear-coat.webp': '100644 blob f0e07689b00b9f15b582dbf851269fdba8254649',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-finish-color-approval-640.webp': '100644 blob '
                                                                                                   '9e0279a3488ad870b777a1062ca6624ecef63076',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-finish-color-approval-768.webp': '100644 blob '
                                                                                                   '412d81915f8f1fc8e6f4ff1a86b449308e39382e',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-finish-color-approval-960.webp': '100644 blob '
                                                                                                   'f7f56d7627a4a9f37bde3fd2d5ed1cea98ee63f2',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-finish-color-approval.webp': '100644 blob a10dd9a5b22f7b4d1ed873a867b87cf4b6088ebd',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-finish-surface-prep.webp': '100644 blob 6a4823022bd9c7c212171485d2220d0bf5921c88',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-hero-640.webp': '100644 blob 37e374bf2aaf8da1612a7d37049c7c6e7f299708',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-hero-768.webp': '100644 blob 4737ef120cb97e4c35de0d149f755ce985755fcf',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-hero-960.webp': '100644 blob 02fdc6c94e38d1e35cdd0ce411f1ab92fe2c7cd3',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-hero.webp': '100644 blob df86d9ad3b362118ed6402cc1afcfa4f81f0224f',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-industrial-application-640.webp': '100644 blob '
                                                                                                    'ab54fb36e0efc5aa06b73bf20221f5bdca857100',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-industrial-application-960.webp': '100644 blob '
                                                                                                    '79608e7b1c9a694f0bd64d0984f22c1db8c12861',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-industrial-application.webp': '100644 blob '
                                                                                                '22085739582ba94b86fd18b5d577bf90ae0c392d',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-materials.webp': '100644 blob f58f96ab2cd13ab0007aeb0e562bfb45092138ed',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-resin-application-640.webp': '100644 blob 301330955e6236687d83c39214efc20ed2a736bf',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-resin-application-960.webp': '100644 blob e3c4532ad89b9e28404b6a7966b1b2197a72c24a',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-resin-application.webp': '100644 blob abdf7e329c9466a95fe2b2b2738f14a54181f73d',
 'Legacy.Maliev.Web/wwwroot/src/images/services/printing/printing-split-assembly.webp': '100644 blob 15999153c1ec42c48ace653760500beac962c48c',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/art/scanning-art-cad-reconstruction.webp': '100644 blob '
                                                                                                    'c4effb8a5302a0d5e67a19e0a63392e767350782',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/art/scanning-art-clean-mesh.webp': '100644 blob c718ad4d6a6796d64971ca8bc7a5d58dfb74ad1f',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/art/scanning-art-deviation-analysis.webp': '100644 blob '
                                                                                                    '46d510f435b6896272c6b4b1c30d22fef27b1a6b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/art/scanning-art-raw-capture.webp': '100644 blob b45a175da17457fc19b103b998c55fcc1f87e3c1',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/cards/scanning-card-clean-stabilize.webp': '100644 blob '
                                                                                                    '8dc4a3c24a40c1947034544955b55a3b894d646f',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/cards/scanning-card-define-outcome.webp': '100644 blob '
                                                                                                   'b5f03fc1a53b667fa020fbbdc05b4291df277a95',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/cards/scanning-card-deviation-analysis.webp': '100644 blob '
                                                                                                       '8bd9c4c89ce9fbb9e774c905fcea0a0698671d12',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/cards/scanning-card-difficult-surfaces.webp': '100644 blob '
                                                                                                       'c3e1c4be10a4f2acf52e0f2afdff78ac0fc3d229',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/cards/scanning-card-environment.webp': '100644 blob '
                                                                                                '3da69abbe95f99679a76dc33d1c205e1a294c5f3',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/cards/scanning-card-previsit-planning.webp': '100644 blob '
                                                                                                      'ecd97f3181dc05aeff639e1732c5a296e6c72ae4',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/cards/scanning-card-raw-mesh.webp': '100644 blob 81b2d575f5978a7fd6a047415efc055ab1d4e15b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/cards/scanning-card-reverse-engineering.webp': '100644 blob '
                                                                                                        'c570c7b09364d6fc37ec4682f9367ea55de10dfb',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/cards/scanning-card-site-access.webp': '100644 blob '
                                                                                                'fc8455c0d29811eb808f5f033ab93b56dafb8489',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/cast-housing-cad-1500.webp': '100644 blob 82dd399dfbe9fb147c3e596c3bb1045579ca42ab',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/cast-housing-cad-480.webp': '100644 blob 4579b0681121e8924297d13e7e88cc1cffc14648',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/cast-housing-cad-900.webp': '100644 blob 83bc46312c04714be34351e1ea8088d080331e7b',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/cast-housing-scan-1500.webp': '100644 blob 074c6c141d9080b245f1e3d25a68d15af5742dd3',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/cast-housing-scan-480.webp': '100644 blob cdecaf3a5d60af9b6d288284a509293d4449ca4e',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/cast-housing-scan-900.webp': '100644 blob af4e7f036c905e9637d6aac4c71daa06336619ac',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/deviation-report-cad.webp': '100644 blob 723e077a3914f00b2d6043c85959470b11c9b0d3',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/deviation-report-map.webp': '100644 blob 4350b3c7541ec41a884992653026a44b82d09905',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/deviation-report-mesh.webp': '100644 blob c91be4ca1b410ad925f8cd65716a9d0655483add',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/statue-color-scan-1500.webp': '100644 blob 9231e17045571e5891c26ffbebef992ab3fee86e',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/statue-color-scan-480.webp': '100644 blob a9e2851635fbebb0e25aee193e74674765a7cb17',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/statue-color-scan-900.webp': '100644 blob 18a42df4ccd970d4e7bf2d2d0db02b8922d9efd8',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/statue-raw-scan-1500.webp': '100644 blob d8fac69a1bdd07289d44c109cc5539fd3b52ea90',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/statue-raw-scan-480.webp': '100644 blob 12ca5159636c5b1f91956708caa19b2a3c97fb45',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/proof/statue-raw-scan-900.webp': '100644 blob f3d9e8281bc583a552ff607b8486ab5b07137bff',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/scanning-hero-640.webp': '100644 blob 1bf5d92b89b51d7fd3a1aa9cbfaceacdefe0e6ae',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/scanning-hero-768.webp': '100644 blob 2d487ad100b554b55660af847d0069fa8142e3cb',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/scanning-hero-960.webp': '100644 blob d05cbcab9af5dff07b8b0ee241b990d6a0e8f0e7',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/scanning-hero.webp': '100644 blob 8b2ed9708d8b57371b7301dd6cb0c440aa74b527',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/scanning-workflow.webp': '100644 blob e9e77e2b94d611b110de940cd2970d44911ff197',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/workflow/scanning-workflow-capture.webp': '100644 blob '
                                                                                                   '4810a3948fbf2bafcefcc4bd4455e5b37be9bf99',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/workflow/scanning-workflow-clean-mesh.webp': '100644 blob '
                                                                                                      '9c705d8d2e84291131a4a9c3c0c1c48f572596a0',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/workflow/scanning-workflow-deviation-analysis.webp': '100644 blob '
                                                                                                              'ccd2c51cf6930d5ecb668ba7d852e47bd5bbf7b0',
 'Legacy.Maliev.Web/wwwroot/src/images/services/scanning/workflow/scanning-workflow-reverse-engineering.webp': '100644 blob '
                                                                                                               '61f6204c484af64410f9f131c5dba74e97053719',
 'Legacy.Maliev.Web/wwwroot/src/images/services/silicone-casting/silicone-casting-workflow-640.webp': '100644 blob '
                                                                                                      '191e334a4d876e9559c322b11f329af00945d5a3',
 'Legacy.Maliev.Web/wwwroot/src/images/services/silicone-casting/silicone-casting-workflow-768.webp': '100644 blob '
                                                                                                      '02f2e5eda41c5ff33d99a06bfeb8a7054551f7b0',
 'Legacy.Maliev.Web/wwwroot/src/images/services/silicone-casting/silicone-casting-workflow-960.webp': '100644 blob '
                                                                                                      'da6ec833f9a0e9983db8afb038a5183dbd7d8186',
 'Legacy.Maliev.Web/wwwroot/src/images/services/silicone-casting/silicone-casting-workflow.webp': '100644 blob '
                                                                                                  'b0279a3e6d32e026745310940b46c670db1ae4f3',
 'Legacy.Maliev.Web/wwwroot/src/images/services/silicone-casting/silicone-mold-prep.webp': '100644 blob efef191baf9a7c26fef5505cc61912966f13cc93',
 'Legacy.Maliev.Web/wwwroot/src/images/services/silicone-casting/silicone-pour-cure.webp': '100644 blob 5534f9b4931111770cc7cda6669b2a42546c62aa',
 'Legacy.Maliev.Web/wwwroot/src/images/sign-quotation.svg': '100644 blob 49b740cfcb789c643325789ecef83c91764fe2f8',
 'Legacy.Maliev.Web/wwwroot/src/images/sla-printing-soon.webp': '100644 blob b71aece08f9d1928bb9c253eb1e35c055ac15473',
 'Legacy.Maliev.Web/wwwroot/src/images/sla-printing.webp': '100644 blob e6f855e1b9420044d844faa5bbbc84394e614fec',
 'Legacy.Maliev.Web/wwwroot/src/images/sls-printing-soon.webp': '100644 blob 0ef73c4f9dc7c73eb42698a59cbe42dbd03c6c92',
 'Legacy.Maliev.Web/wwwroot/src/images/sls-printing.webp': '100644 blob 9d6acc148f4ee486d7be39f809928b9c5c19f750',
 'Legacy.Maliev.Web/wwwroot/src/images/tpu-3d-printing.webp': '100644 blob fb6c802ecc81290c071da70e511263668866b074',
 'Legacy.Maliev.Web/wwwroot/src/vendor/@3dconnexion/3dconnexionjs/3dconnexion.module.min.js': '100644 blob 98ced2c473930fce10b25afcac1c28ca4cfb8bc2',
 'Legacy.Maliev.Web/wwwroot/src/vendor/@3dconnexion/3dconnexionjs/LicenseAgreementSDK.txt': '100644 blob 677ea341bcc407d15836c91f527e7496e7fa59fa',
 'Legacy.Maliev.Web/wwwroot/src/vendor/three/LICENSE': '100644 blob 5303437e406ed22d8cb0b4c12398870792a25878',
 'Legacy.Maliev.Web/wwwroot/src/vendor/three/OBJLoader.js': '100644 blob 93e8ba1cc564f55d2074a443588357a6a747c0d2',
 'Legacy.Maliev.Web/wwwroot/src/vendor/three/OrbitControls.js': '100644 blob 0873097102ca98527d3f2b5132a57b0b9d62ce07',
 'Legacy.Maliev.Web/wwwroot/src/vendor/three/STLLoader.js': '100644 blob 5c50fc2db0065b928e3aaa7e74d9414fdc5c2c18',
 'Legacy.Maliev.Web/wwwroot/src/vendor/three/three.js': '100644 blob d855ea0aeea1afb894cc8e0c6c6a2b2f3c15aa96',
 'README.md': '100644 blob b648780de74bbcdd63651859a77cbc1a98056962',
 'acceptance/CareerOriginalMaterialization/CareerOriginalMaterialization.Tests.csproj': '100644 blob 9105df41e793cf7c1fa7a41154f3a11f8eb002e3',
 'acceptance/CareerOriginalMaterialization/OriginalMaterializationTests.cs': '100644 blob 73577a53df3ec21abfdc02e431a18652aee21aa0',
 'acceptance/CareerOriginalMaterialization/OriginalSource/Level.cs': '100644 blob 6368f3f3680cbf7fdbad47280ad27317ce6fcd3b',
 'acceptance/CareerOriginalMaterialization/OriginalSource/Offer.cs': '100644 blob 3ad03e62453c9c72f5c807e4a61ed96433afb129',
 'acceptance/CareerOriginalMaterialization/README.md': '100644 blob 0f67aa5d8ed4ec021b0369953758ce6132a0edc6',
 'acceptance/CareerOriginalMaterialization/source-provenance.json': '100644 blob 346581b501eb078512fcfc27e831f17eb989bc25',
 'acceptance/MemberCompanyCatalogPersistence/CatalogHost/MemberCompanyCatalogHost.csproj': '100644 blob 4a18a0f7fc3d5cae5483a35d09b20749ef01f544',
 'acceptance/MemberCompanyCatalogPersistence/CatalogHost/Program.cs': '100644 blob 1e85d1f536ee304ba4d15e4b1d66c164bcea768e',
 'acceptance/MemberCompanyCatalogPersistence/README.md': '100644 blob 475d88327eb3caf0784c71b604796c2473a3dbc9',
 'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistence.Tests.csproj': '100644 blob '
                                                                                                  '4a16afea49d08f32d019a23e0766580024d40343',
 'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistenceTests.cs': '100644 blob e4b813901dbe30f48a5625dd69fa90aa0a33222b',
 'acceptance/MemberCompanyCatalogPersistence/Tests/SyntheticProvider.cs': '100644 blob 635e4380f0e83a951f17a3bc855d6103a26255a4',
 'acceptance/MemberCompanyCatalogPersistence/retain.py': '100644 blob 2e4c024e1562706ad43374cbdec5222c62b7c8f9',
 'acceptance/MemberCompanyCatalogPersistence/test_retain.py': '100644 blob 008b5ed1a5d3d3ef2675fe98989e64eeaff1e78f',
 'docs/account-noindex-header-design-20261001.md': '100644 blob dbf8e1a7784ae4947aa8354aee4c6e9f744e0e07',
 'docs/account-source-pin-secret-scan.md': '100644 blob 8317886f93e7e5532dbff042d6c502d42e4ede10',
 'docs/account-utility-seo-bundle-design-20261001.md': '100644 blob d445c7fa0d864f2157dc3817da30e566597651ee',
 'docs/additive-authenticated-profile-consumer.md': '100644 blob 1590ab0687e92ac8f89f8e40a840387088884946',
 'docs/additive-commercial-design-20260930.md': '100644 blob fe1ef99ff3b0b9b8f5d2c0c86c71e8bfac417c6c',
 'docs/additive-physical-failure-meaning-design-20261002.md': '100644 blob 2b9cdd51d71b8c6d73ea4a56d960b0ccb6d6cbb8',
 'docs/additive-quotation-evidence-2026-09-19.md': '100644 blob 76bf4464d3cf06a5a66b833397d353d62e128900',
 'docs/analytics-validation.md': '100644 blob 0d4a26703fad9bf8487028073a3db0e2c589065d',
 'docs/blazor-migration-performance.md': '100644 blob 3c8c03563f89a4ac1f547ecf2f122e07973a27a8',
 'docs/career-client-first-original-read-contracts-20261022.md': '100644 blob e0b1bb887000eb311e416bf728efda63108f33b3',
 'docs/channel-quality-measurement.md': '100644 blob 1dd34c7e682cb92b9b435ff2f6532349e9a04333',
 'docs/cnc-authenticated-profile-parity.md': '100644 blob ff32c0ee03e78113fa008f4a1b202dbe18c78045',
 'docs/cnc-file-admission-parity.md': '100644 blob 553f24f5a071415309cb2b0af31e614b3bd34a7f',
 'docs/cnc-file-finalization-parity.md': '100644 blob 07fe28cb87915c7b4f675525d2228f9fcc4a7849',
 'docs/cnc-notification-contract.md': '100644 blob 118022bc3c89216f89c65bb93ec6afc73245d80e',
 'docs/cnc-planner-migration.md': '100644 blob a55807128bd53d1975d609762831609ba5d30624',
 'docs/cnc-profile-fill-only-persistence.md': '100644 blob 7ddedd01108a2cf667790278d691b20716736053',
 'docs/cnc-request-transport-parity.md': '100644 blob 7d81f87265ebda1d4e579c2ece3d7a40a8e498e9',
 'docs/cnc-signed-link-contract.md': '100644 blob 469473a17e0d0b7ae18b0d15eb78d3de27575f7d',
 'docs/cnc-source-commit-reconciliation.md': '100644 blob fa7098a93a5110b0e4eab0fa3398ce9017e36898',
 'docs/cnc-stock-foundation.md': '100644 blob 3f8ea694329e4ed6b5d17562180d1a13e9cb1c36',
 'docs/cnc-submission-admission-parity.md': '100644 blob eec6e9837e17c789e43252b2e02efabc2f3564ad',
 'docs/complete-source-history-parity-through-8049024.md': '100644 blob 679bd03c35cefca092efb5c9cbe199553a19c31e',
 'docs/corporate-logo-webp-qualified-assets.json': '100644 blob 8212365effb1d22f33b931b40a58fbf4c8f67b70',
 'docs/crawlable-email-fallback-design-20261001.md': '100644 blob 9f47ce95108c41e77305aad2bda347c34d12b103',
 'docs/esd-fulfillment-proof-20261002.md': '100644 blob faca1cb7f321bab7d4c053990560edf617434be2',
 'docs/esd-material-profile-parity-20260930.md': '100644 blob b63e656d7c3acea3862a1bd4e6eee0c30664035a',
 'docs/finishing-color-design-qa-20261001.md': '100644 blob 000a8b901e2f3c9736cebea632e1a5bad7e8510a',
 'docs/localized-canonical-cookie-design-20261001.md': '100644 blob a492d535fd22cb469528f458483c3e8dd74f7809',
 'docs/material-display-renderer-design-20261002.md': '100644 blob 291d258b06a43f1a56bbf39c74e9501965abd5f3',
 'docs/material-identity-migration-20261006.md': '100644 blob 6a1f461994061db692cebc7db94a0a99cf83ba0c',
 'docs/material-price-progress-test-first-design-20261002.md': '100644 blob ade3ffe99a5103093758d3f2e78ee22b3c9b78a9',
 'docs/material-pricing-display-consumer-design-20261002.md': '100644 blob 921cc48a57e34584b805282b0b13ff458222bfa3',
 'docs/material-pricing-lifetime-design-20261002.md': '100644 blob 3586a6b0228b4088aa6ec5f114eb82cf4403a9be',
 'docs/material-progress-backend-proof-design-20261002.md': '100644 blob 6d1b097dd8edfec28bbca6280913937e6a953119',
 'docs/member-auth-indexing-proof-design-20261002.md': '100644 blob 15ed2aced5516bba92eb15f44c750d2874c9d208',
 'docs/member-billing-catalog-persistence-proof.md': '100644 blob 4a80386ae153e479f86469a7ba3d5b6df7310e9b',
 'docs/member-shipping-catalog-persistence-proof.md': '100644 blob b770a56a24c4e954f9e5d8bcad397e4532f3a854',
 'docs/migration/about-customer-row-original-logo-sources.json': '100644 blob 9e4750ed6dd017ce877aadfced3810854803d5fa',
 'docs/migration/about-customer-row-qualified-assets.json': '100644 blob 56cac892d8a6b4d82c7d4336a689f3ec23feca15',
 'docs/migration/account-localization-rendered-seo-20261006.md': '100644 blob 26d396f38f3501215497cf352eb46cbdd39c1d68',
 'docs/migration/company-release-assets.v1.json': '100644 blob 6b55e8dd05e009b34fcaeadfd1164a88fd189ed3',
 'docs/migration/inquiry-localization-20261006.md': '100644 blob be0e91e56dd0015c38b6cc15c8372f3b8914ac67',
 'docs/migration/knowledge-612-613-614-source-ledger.v1.json': '100644 blob 28b811ce3f6deeb7ad377ef6b90511cbcdf57af8',
 'docs/migration/knowledge-770-source-intake.v1.json': '100644 blob f1247781f96b4e2c57639b4c3da8528914fde1f0',
 'docs/migration/knowledge-770cb580/source-provenance/2026-10-16-knowledge-diagram-captions.json': '100644 blob '
                                                                                                   'd4656e473875655da527905e7a4ad670312fda34',
 'docs/migration/knowledge-770cb580/source-provenance/2026-10-16-knowledge-fact-audit.md': '100644 blob 253d94e832e81cb2e1925a31cec580aa602b2c42',
 'docs/migration/knowledge-770cb580/source-provenance/2026-10-16-knowledge-format-error.md': '100644 blob 40e9f93286224fc5f9d0260f8e3bf40153764724',
 'docs/migration/knowledge-770cb580/source-provenance/2026-10-16-knowledge-photo-prompts.json': '100644 blob '
                                                                                                '310e9e472a0e4b96ab647463c8afd55875e92420',
 'docs/migration/knowledge-770cb580/source-provenance/2026-10-16-knowledge-reading-images.json': '100644 blob '
                                                                                                 '6e187a6fbeb86929a2dac4ec85862225587d0193',
 'docs/migration/knowledge-770cb580/source-provenance/2026-10-16-knowledge-reading-layout.md': '100644 blob 8a3f6cb923ab30422da1dc087b07a72e5b18da16',
 'docs/migration/knowledge-770cb580/source-provenance/2026-10-16-knowledge-search-and-hover.md': '100644 blob '
                                                                                                 '439929828950e9b4ddc9c58b416bcc238489c6af',
 'docs/migration/knowledge-770cb580/source-provenance/2026-10-16-knowledge-visual-corrections.md': '100644 blob '
                                                                                                   '4633827aeb6c66e16003ebe396d4090fb7ece00d',
 'docs/migration/knowledge-770cb580/source-provenance/2026-10-16-knowledge-visuals.md': '100644 blob e5d0034bfb5fcbbc06a8d96ede7aa30cc9cbd449',
 'docs/migration/knowledge-770cb580/source-provenance/2026-10-16-manufacturing-knowledge-library.md': '100644 blob '
                                                                                                      'eb99f3abc3001183cbeb93f19b63b0ffba9d66ed',
 'docs/migration/knowledge-770cb580/source-provenance/2026-10-17-knowledge-production-release.md': '100644 blob '
                                                                                                   'b645ea88d537cb55c771e5076c2ca9547586b171',
 'docs/migration/knowledge-assets.v1.json': '100644 blob af0639089ef33a8c7539da4074c2c0705dba7b10',
 'docs/migration/knowledge-source-intake.v1.json': '100644 blob 5fd485eaecf9ef343e588ce2b737088d8bddd778',
 'docs/migration/knowledge-visual-source-export.md': '100644 blob 0f48a49c9bb5d53fc2de64d26647de7f2d067b76',
 'docs/migration/material-completion-budget-contract-20261007.md': '100644 blob 316ba4efb908c7e39c1dc062c21193c9be70cf1a',
 'docs/migration/preliminary-dfm-snapshot-20261006.md': '100644 blob fecfdbce50529a5581e24ae94b396f058093aca4',
 'docs/migration/preliminary-duration-vector-browser-20261007.md': '100644 blob 18790799273a18e349009f502fda533c8ee7c405',
 'docs/migration/preliminary-pdf-browser-20261006.md': '100644 blob 865263104bf9d165b5e7acc53509d720e9f7312a',
 'docs/migration/scanning-comparison-browser-20261006.md': '100644 blob 4ef8b7f36f864b8d4396bdc5b860f647367daf64',
 'docs/migration/source-3c052-about-presentation.json': '100644 blob c24458be4065049cd086ae33e2f6c62e0f6276c6',
 'docs/migration/source-d7bb-d538-about-customer-row.md': '100644 blob c73fd2dc35c4ac3517b3f9e8fbfa6eb1b824266f',
 'docs/migration/summary-navigation-diagnostics-20261007.md': '100644 blob 80aed516205482824539edb0ec46585dbda4dc9f',
 'docs/migration/web-598-599-600-integrated-candidate.v1.json': '100644 blob 36fcde0695eccf5eed529c6374126201cb987017',
 'docs/nine-upload-selected-duration-failure-diagnostic-20261002.md': '100644 blob faff7cdcf7d9703e9d9dec50694fd7714571d119',
 'docs/no-weapons-policy-sources.md': '100644 blob ec5cb5a41aa3021fbfa1e601f15c7e56d8dad301',
 'docs/protected-geometry-profile-continuity-design-20261001.md': '100644 blob 2bb7a6b580952a698b7aea8e8d19248ffd1af228',
 'docs/public-crawler-inventory-proof-design-20261002.md': '100644 blob db0ad7639c7de4e9de2a68558239a93d83cfdd32',
 'docs/public-document-verifier-quotation-design-20261001.md': '100644 blob 51b84076287fa4e5a69f5e7c1cefa16cf8fdb821',
 'docs/public-service-coverage-rendered-proof-design-20261002.md': '100644 blob 3a0dff8ea355f363f7a69a843513d607daf6741b',
 'docs/quotation-country-resilience-migration.md': '100644 blob e8c92256fe95ac5ca7398e9d3c1f63897df43103',
 'docs/recaptcha-configuration-acceptance-20260930.md': '100644 blob bda00c65428361c8eb971e6f385a02ad8d0afa07',
 'docs/resin-server-evidence-design-20261001.md': '100644 blob 297057d35a461b101d64dbebadef500cc49d37ad',
 'docs/scb-payment-service-boundary-design-20261001.md': '100644 blob 763ee29ffb46adb63ae1db2d43d899045b4abcee',
 'docs/search-console-cohort-reporter-acceptance-20261001.md': '100644 blob 709145568f2d4fdc6da9fafe15176d67b96d4b8c',
 'docs/seo/2026-09-26-3d-printing-ctr-decision-pack.md': '100644 blob e38a6272a9c71453376a54215038404c1c7abd21',
 'docs/service-coverage-material-progress-combined-acceptance-20261002.md': '100644 blob 82818308f194f7c16169fd4af1ccd324101d3a38',
 'docs/sitemap-response-cache-design-20261001.md': '100644 blob 25e546e0e3e298557c40e4d7948161e5d9540c1e',
 'docs/source-commit-register-through-5ac7d04.md': '100644 blob 3caee05b5432119ff642ed9823a8fde9211d7f6e',
 'docs/source-parity-checkpoint-2026-09-15.md': '100644 blob c896256e81019f430c3e7e2013445220354e40d3',
 'docs/source-parity-daily-checkpoint.md': '100644 blob 758829846609e36e103380e558368111ca29835c',
 'docs/source-parity-delta-through-4486f0e.json': '100644 blob 888ace3722f4d756f39ece298d5bcfdee17b580a',
 'docs/source-parity-delta-through-7c416cc.json': '100644 blob f495c6388abbee4dda13dda5f23622f4e1ee2a4c',
 'docs/source-parity-through-8049024.md': '100644 blob 0b930dd6cf87c07c680377dcff0f9588d158630c',
 'docs/startup-security-migration-20261006.md': '100644 blob 632f62d9de1da76f570307a2551035892eb8647e',
 'docs/strength-profile-pricing-migration.md': '100644 blob e9768a0b3b265e9da638f740385aff33ff9f1ca3',
 'docs/summary-transition-browser-design-20261002.md': '100644 blob 22f0be66196742a0ddefbcc1adede686cec8539f',
 'docs/superpowers/plans/2026-07-16-change-email-confirmation-static-ssr.md': '100644 blob 241056c086000d617f18c892ad13b911a8ee6cbc',
 'docs/superpowers/plans/2026-07-16-error-static-ssr.md': '100644 blob 83f623873db82fa9e6e47941df1c54256e42e5af',
 'docs/superpowers/plans/2026-07-16-knowledge-static-ssr.md': '100644 blob 5561003f38ff4dfb367a3b35b30b02893ee26fb9',
 'docs/superpowers/plans/2026-07-16-logout-static-ssr.md': '100644 blob 69010238ac2c85f0f2967832a6b9c4dfa403d677',
 'docs/superpowers/plans/2026-07-16-member-account-static-ssr.md': '100644 blob eaa4b9f2ef1611a95bc5dcd5790971548df94642',
 'docs/superpowers/plans/2026-07-16-member-landing-static-ssr.md': '100644 blob 0544219593a099d0fd24941659b329135199e725',
 'docs/superpowers/plans/2026-07-16-member-orders-index-static-ssr.md': '100644 blob a3d270043adbfe46523bead59d960c29c53d2a87',
 'docs/superpowers/plans/2026-07-16-member-redirect-endpoints.md': '100644 blob ad83db7129ab725c8cf09ab00c1eef02f8aab204',
 'docs/superpowers/plans/2026-07-16-sitemap-endpoint.md': '100644 blob 586c0d8eafbd4832634655674d94aad76de3f49f',
 'docs/superpowers/plans/2026-07-17-3d-printing-static-ssr.md': '100644 blob 06525ff1f09610ad8c4c2ab7407ac5cc58fb24cb',
 'docs/superpowers/plans/2026-07-17-3d-scanning-static-ssr.md': '100644 blob ed37d1295dff58cca5ad370b50557225e0b6b744',
 'docs/superpowers/plans/2026-07-17-cnc-machining-static-ssr.md': '100644 blob f45fa479d06d91c399903dcfea7666ecadfed0fb',
 'docs/superpowers/plans/2026-07-17-member-quotations-index-static-ssr.md': '100644 blob e2886d5de4cf3bba33dace3b9979de6e9e66a1cf',
 'docs/superpowers/plans/2026-07-18-channel-quality-measurement.md': '100644 blob b905073cc605d00fbebc7978cc057733aa702b28',
 'docs/superpowers/plans/2026-07-18-cross-channel-analytics-contract.md': '100644 blob ea92aceae10007e6a5c0ab75dc8be9bc42cff946',
 'docs/superpowers/plans/2026-07-18-instant-quotation-parity.md': '100644 blob 72158c9900a9874ea1c4f3954848adad64fbf26b',
 'docs/superpowers/specs/2026-07-18-instant-quotation-parity-design.md': '100644 blob 92ddf6487d8352459765ddec7b4f8ad318acb822',
 'docs/superpowers/verification/2026-10-17-process-illustrations.json': '100644 blob abac4d4f539556012a6bd0c28926fc9aca378dd8',
 'docs/thai-lookup-consumer.md': '100644 blob fc89e02bd4373bcabceb2224e1aa04e5cc00329f',
 'docs/wall-thickness-parity-safe-preview-design-20260930.md': '100644 blob 6102a288f53036240dba31f75a87eb76700ab6cd',
 'docs/wall397-browser-acceptance-design-20261001.md': '100644 blob 4e138eed7fc3dde2bc0f28a73a3361c24402298a',
 'docs/web-account-failure-transport-policy.schema.json': '100644 blob aeecee16758392b017b0d2c892e088e0ffb353c8',
 'docs/web-sealed-source-transport.md': '100644 blob 4f096c3a432ce545faa7a666b8febdead32823e8',
 'docs/web738-residual-ownership-and-custody-20261022.md': '100644 blob d67c54eb5681eeb28ce1e3f62fd96605de59ea30',
 'docs/websurface-physical-webroot-ownership-20261022.md': '100644 blob c9c69573de2bde06f9feb96d1c99e53367babb85',
 'nuget.config': '100644 blob 765346e5343d46ac83654843869dc06b65b2bdc4',
 'scripts/career-focused-identities.json': '100644 blob 7f0634915b8650d15e1bba60a8bea4fadfa1daed',
 'scripts/check_web_candidate_admission.py': '100644 blob 39dba751a0516d01c967049965320d7b9e75156e',
 'scripts/launch_web_capped_kernel_proof.py': '100644 blob 108feb042367d012890dcc30b13cdc47a7fa52c5',
 'scripts/materialize_web_candidate.py': '100644 blob acfc13a166646358e39ce9260fb63a5fe034bb99',
 'scripts/measure-local-web-performance.ps1': '100644 blob 4ba1b086b2e7aceff5cc7b8d6464bd2a827f5557',
 'scripts/prepare-billing-catalog-proof.ps1': '100644 blob 11498f9bd1a12faffd4c158c1cb59874d7fcbbf7',
 'scripts/prepare-member-auth-indexing-proof.ps1': '100644 blob 67d5fb86f2f74ccd90f66e59d6728b86ddad1e7d',
 'scripts/prepare-profile-producer-boundary.ps1': '100644 blob 168157bd9246e9073518af342ce0e245e597c2a7',
 'scripts/prepare-scb-payment-boundary.ps1': '100644 blob 9c1b133116795dee54c8c6b3f4ccc07b8351eacf',
 'scripts/prepare_qualified_web_profile.py': '100644 blob 3ececf640d2a901aab5d98edd798a167fd270524',
 'scripts/probe-startup-dockerhub-quota.py': '100644 blob 85ba1017343afe3cb387deae4a9f03c712e76c04',
 'scripts/prove-about-customer-row-webp.py': '100644 blob ccb25d154c34110a45e4ca07ab4dcea72eaf4e5e',
 'scripts/prove-corporate-logo-webp.py': '100644 blob 1a729ae1167675b07717b8809fa9c493ae32e24d',
 'scripts/retain-billing-initial-probe-comparison.py': '100644 blob 6fe5d26b35848e84adc3141ff7b196d9891d590d',
 'scripts/run-fx-joined-hosted.py': '100644 blob 2b677e24574d08ece4003b25fb48aac1befed50b',
 'scripts/run-web-packaged-hosted.py': '100644 blob ce54bc7516d940cfb1afab021b49c506c7b3e7e5',
 'scripts/run_web_account_candidate.py': '100644 blob a4f8903b97a1dae6b78ed11189a4e551fc5512f8',
 'scripts/run_web_candidate_phase.py': '100644 blob f483a013f68d209593e6901891a4d57ce89e0eca',
 'scripts/run_web_capped_kernel_proof.py': '100644 blob c89e49a48969a049f7a52f72d0c902f141fe5925',
 'scripts/sealed_source_capsule.py': '100644 blob cab6997ff2af48bdd4ac7e9240cd12d01e5b3190',
 'scripts/stage_knowledge_assets.py': '100644 blob d0d50098830610c9b6925fac4d4a42303be8bf91',
 'scripts/tests/test_billing_receipt_attempt_custody.py': '100644 blob f2599691099861f52ad3ccfd7430d4bf48870e05',
 'scripts/tests/test_country_web_candidate_admission.py': '100644 blob 74a8de96bf7b64686d5425c001498ca8b4ef4d68',
 'scripts/tests/test_kernel_web_candidate_owner.py': '100644 blob bfb916971c32553a7f5d77fd0cf91a968bf73c68',
 'scripts/tests/test_materialize_web_candidate.py': '100644 blob 5ed2842b33f9b17031218b75eb2c25f909f273cf',
 'scripts/tests/test_sealed_source_capsule.py': '100644 blob 347d87b104a9bc9a3df60050bb5aa0d1ae0878f0',
 'scripts/tests/test_startup_dockerhub_quota.py': '100644 blob b4f3c38b3b88aaa32f981bfa1f2b7b9a987c6aaa',
 'scripts/tests/test_web_candidate_account.py': '100644 blob a5ef9b95de8a633d45b21db35f54c772375a0cd8',
 'scripts/tests/test_web_candidate_admission.py': '100644 blob 24c383ffdfe028e317721cece8f882e7f83fc1c3',
 'scripts/tests/test_web_candidate_custody.py': '100644 blob 838432e574d7a320a41bcdb1ef203443e456883b',
 'scripts/tests/test_web_candidate_email_session.py': '100644 blob 0753586d2b0f0ed62ccf6ba880df38726cda18fa',
 'scripts/tests/test_web_candidate_phase.py': '100644 blob 2cbcbe6f6d965709598d4e5958cd36a6a2f44b31',
 'scripts/tests/test_web_candidate_secret_scan.py': '100644 blob 34707afec38e81359e7f0d91fb11df2ec290e03e',
 'scripts/verify-billing-persistence-proof.py': '100644 blob 41972b01df4c1f65c251df0a76f0187e53781fac',
 'scripts/verify-career-focused-trx.py': '100644 blob 6f4945bcc2071c4628fb67b1d649b05d267ad659',
 'scripts/verify-comparison-callback-regression.py': '100644 blob ac7923b5b10e50e01fce8ec9287b223f5b2d5158',
 'scripts/verify-complete-source-history-parity.ps1': '100644 blob 70779c2ea06c56ac7dc40341be8a112e8e1a94a8',
 'scripts/verify-focused-web-trx.py': '100644 blob 9f249235eb32e89f45f543659d6011844c789e1b',
 'scripts/verify-fx-browser-assets.py': '100644 blob 3bc8f7c5696b51a5a6c726c3a3201db8a5b02767',
 'scripts/verify-preliminary-pdf-proof.py': '100644 blob c06286bce3a5b5505d2b571924d891b828ccab87',
 'scripts/verify-shipping-persistence-proof.py': '100644 blob cec940bfe054cbee150715a7e19a5a357e56bec2',
 'scripts/verify_company_release_assets.py': '100644 blob e1ae5e1f4c7f33e26d585a2d0f885f46e94958e6',
 'scripts/verify_knowledge_assets.py': '100644 blob 84bc8c2e634becd683b1da71a3c22dd1386263a1',
 'scripts/web-account-failure-policy.json': '100644 blob 6e88b2dc36e71950184788a5f01e0eeb52759fcb',
 'scripts/web-candidate-policy.json': '100644 blob 0a69b0f66da3a6138de46e20a5392f3c82a4e60b',
 'scripts/web-country-policy.json': '100644 blob cb84cb90a6f2e1827a3bda82526f1a92916ce286',
 'scripts/web-email-change-session-policy.json': '100644 blob 7162d25d430293224ee7a6df9ab2f3bf46d2b077',
 'scripts/web-full-test-observer.bash': '100644 blob 8c29041a334b441080ca47085ee47e3a9f8d00bf',
 'scripts/web-full-test-observer.py': '100644 blob 678e2daa67c5f04e2d665c8fcc83f704c7648a05',
 'scripts/web-optional-tax-build-policy.json': '100644 blob 8a1b2af61bbe0ec2b3378b029f593404723d61fc',
 'tests/CareerFocusedTrxGate.Tests.py': '100644 blob 506547676f2e4006923b696aad0fa8f1dd85c020',
 'tests/CompanyReleaseAssetValidator.Tests.py': '100644 blob 4828674b4db33f0a94ea58bb805552b0b91eab78',
 'tests/ComparisonCallbackRegressionGuardTests.py': '100644 blob 1d8a0019dc1db47d8c736de0ee172ab0a6ba356a',
 'tests/CorporateCustomerRowWebpProof.Tests.py': '100644 blob f99cad0035a8991b572131c68e155a8c5205a3e5',
 'tests/CorporateLogoWebpProof.Tests.py': '100644 blob 3354a17ff9a8a4ba30c8a82754ddb8a645140190',
 'tests/FocusedWebTrxGate.Tests.py': '100644 blob a4578ebfea2501e611d94059e32ee0d5e9b773e5',
 'tests/FullTestObserverControls.py': '100644 blob 8cc7947918ef54b238effd2738f99a3568cbadb6',
 'tests/KnowledgeAssetIntake.Tests.py': '100644 blob 1680ad23b207cfa8af0014fffb8d6260577fa147',
 'tests/KnowledgeCurrentAsset.Tests.py': '100644 blob 98c292d576cade0ba3dbb018fc0bc9ad733f8864',
 'tests/PreliminaryPdfProofGuardTests.py': '100644 blob a9dd3cf568cdebec2ec5bb8cad1638ccf9618e45',
 'tests/ProductionSeoVerifier.Tests.ps1': '100644 blob a2975faa2adf1fe7ade1b0a475c56398bbbd86b1',
 'tests/ProductionSeoVerifier.psm1': '100644 blob b7b23d2ddd9ad657ac057c5d8d6c56362754aeec',
 'tests/SearchConsoleCohortReport.Tests.ps1': '100644 blob 74953c23a10824622893ebf2d507eb2bc02494ea',
 'tests/SummarizeSearchConsole.ps1': '100644 blob 553af237c62a45928ea2d5487690fde54e7f0ece',
 'tests/Test-WebFxRawCoverage.ps1': '100644 blob 9d1b4356ff99313e3430307ea58b0503a7f75cfd',
 'tests/UtilitySeoVerifierProof.ps1': '100644 blob abdd44805a8cb6680e0cd9640214a49e19411bd1',
 'tests/VerifyProductionSeo.ps1': '100644 blob 33d40b7160b09d3e0d2ce9c16f39b593d1f9940d',
 'tests/WebFxRawCoverage.Tests.ps1': '100644 blob 5fd86eb56f5e36a9729212799c777df5cc00dbfe',
 'tests/WebPackagedHostedGuardTests.py': '100644 blob b0e75bac2f5f4af135ba30267af84bb1ee88af4f',
 'tests/about-customer-row-source-inputs/alten-korea.png': '100644 blob 74208d82f3ff4cd7534147a6af147e0487430d4a',
 'tests/about-customer-row-source-inputs/crown-ceramics.png': '100644 blob b9f8104db45442cbbf598a6a36d5649a159435d9',
 'tests/about-customer-row-source-inputs/do-day-dream.jpg': '100644 blob d6bad8f3f09957c50e2e6aeba4256e29c4f08d13',
 'tests/about-customer-row-source-inputs/forth.png': '100644 blob 0ca7acc8723ef04ab74d7ed7c16cf5bf2c388d4a',
 'tests/about-customer-row-source-inputs/index-creative-village.png': '100644 blob 5fd513750b170e0173d875b14ebc765cbaa802ba',
 'tests/about-customer-row-source-inputs/sahamitr-pressure-container.png': '100644 blob 0001a5351842ab151a18085362ab474aa761262c',
 'tests/about-customer-row-source-inputs/thai-agri-foods.png': '100644 blob aa58542c8e1c710986939356805962e71cb36e2e',
 'tests/about-customer-row-webp-inputs.json': '100644 blob 35e5cf98997ccb2baa13563fcca8f5212fcdde06',
 'tests/corporate-logo-webp-inputs.json': '100644 blob 9ab07a0b8bbcfa70e5e0b28095f2b864545c48a3',
 'tests/corporate-logo-webp-requirements.txt': '100644 blob f1965c71b8d4333a8668a3468304fb3e85b2edc6',
 'tests/corporate-logo-webp-srgb-profile.LICENSE.txt': '100644 blob 8fc8ff030705be99ccc005a9a35c8e0151c70348',
 'tests/corporate-logo-webp-srgb-profile.json': '100644 blob bfdf7053ceb1d56860be5ec550c374b5bba4a109',
 'tests/fixtures/search-console-queries.csv': '100644 blob 494e7c9a4b9ef475cbca21305390ad0074aba8b8',
 'tests/fx-joined/FxJoined.Tests.csproj': '100644 blob 1b6bbffc2d65ee2f8ed2f66c965ff1f9e630f4ec',
 'tests/fx-joined/JoinedFxNativeHttpTests.cs': '100644 blob edbd7c6676863b2c80163dddd752d6655d655a49',
 'tests/fx-joined/README.md': '100644 blob 0245a5fece93f21e223da96354f08ea3a261c9e8',
 'tests/fx-joined/candidate-graph.json': '100644 blob 489ce73b1cdf40ff9d246042d95f60d4024eaa82',
 'tests/fx-joined/provider_fixture.py': '100644 blob a6109921fa53c73e8b396661d0dc54903044a5f2',
 'tests/fx-joined/test_browser_asset_guard.py': '100644 blob 6e60ff1ff42f9853dd3f9b66cb5a747ae2449669',
 'tests/fx-joined/test_orchestration_guards.py': '100644 blob b10cf37d684d09919b1bcc1f5f7eacc62d2a0a4e',
 'tests/preliminary-pdf-requirements.txt': '100644 blob f2f816d4a481ae7f15f2346ad983b13caa03e87f',
 'tests/web-fx-generated-inclusive.runsettings': '100644 blob e6a56ba764d9c9ebb1429ea21f1b8b324a400c78',
 'tools/additive-benchmark/AdditiveBenchmarkCli.cs': '100644 blob d1ca73788948db833ed53adeb2515ac2f2765498',
 'tools/additive-benchmark/AdditiveBenchmarkRunner.cs': '100644 blob b4d4814499147bc0afacf559d217f40bc9fdbe67',
 'tools/additive-benchmark/BambuStudioCli.cs': '100644 blob aa783b6d40a33287a5e05da0a1d3a5b6dbb8959a',
 'tools/additive-benchmark/BambuStudioProfileResolver.cs': '100644 blob a26a4b55a023c523b5156d8cef018cecb4eb1912',
 'tools/additive-benchmark/BambuStudioResultParser.cs': '100644 blob d66433601ca6bb3e9e42d6c3d9d2cbe1a8b5b565',
 'tools/additive-benchmark/FilamentProfileCatalogValidator.cs': '100644 blob 242012c98018a25bea08f5409aac86bca449b1dd',
 'tools/additive-benchmark/Maliev.AdditiveBenchmark.csproj': '100644 blob dab6122f1e5f0201236fb25fb99aadc51c3f9606',
 'tools/additive-benchmark/Program.cs': '100644 blob 86d456cab246af4c2055ae1d9cb87f4783664eb6',
 'tools/additive-benchmark/README.md': '100644 blob 4809a135b082546e09d91a5c8415982a0d7756b3',
 'tools/cnc-catalog/.gitignore': '100644 blob 00930dff3823e6f6fe1e43cd90f867ef6bf8197c',
 'tools/cnc-catalog/README.md': '100644 blob 72860f9ceabb86507e124be5ed6db49b277b9bea',
 'tools/cnc-catalog/catalog.json': '100644 blob 02f991937f3c496e87b3b07f501d69f2bd73f15f',
 'tools/cnc-catalog/catalog.schema.json': '100644 blob 651d483db0da6ca9fe50bf9e90b198ab17c2062b',
 'tools/cnc-catalog/validate.cjs': '100644 blob 7e37fdea73438a2f738dfb01da486b0bd2965f77',
 'tools/cnc-kernel/.gitattributes': '100644 blob 8a4e4aab9bd1619998fd38af640974142cefb9be',
 'tools/cnc-kernel/README.md': '100644 blob 2c61035dc71e4b94767a794a6e3a211996e85029',
 'tools/cnc-kernel/REPAIR-MATH.md': '100644 blob a363b05f8500c36210d98eee463ece216542f8b7',
 'tools/cnc-kernel/apply-document-overlay.cjs': '100644 blob 62cd238a000a51532f8c8f39a6cf2ecfd84a6d2d',
 'tools/cnc-kernel/apply-overlay.cjs': '100644 blob d126722d2043de868d20be96b5eacdb10e3a6c7e',
 'tools/cnc-kernel/apply-repair-overlay.cjs': '100644 blob 639bfb6ef7f3631b81871da76aaf12fb7f6c0337',
 'tools/cnc-kernel/apply-repair-source-bounds.cjs': '100644 blob a7f7e12a1a54705eacf154aebb257089a0408515',
 'tools/cnc-kernel/apply-target-query-overlay.cjs': '100644 blob ace3697fa404d9731931f98b6c0349ea604ff12c',
 'tools/cnc-kernel/apply-target-query-profile-overlay.cjs': '100644 blob c1d3010203e74a30cdf8ca77157930b1b7683cc7',
 'tools/cnc-kernel/apply-warning-lineage-overlay.cjs': '100644 blob d00e60792f0ef89d6016bc6334b81d7acb78d325',
 'tools/cnc-kernel/build.ps1': '100644 blob c7c3b268220acdf64d86f7dba0c89594a1c9c3bc',
 'tools/cnc-kernel/kernel-body.hpp': '100644 blob 0c2ace9dc54fff9d0b40523eda8ce5503cb845e9',
 'tools/cnc-kernel/kernel-circular-revolution.hpp': '100644 blob 262265a3d8f924d6b12c57d03db05cd779a52e1c',
 'tools/cnc-kernel/kernel-document.hpp': '100644 blob d58c6c910b839c727b6da0f082374080a1f99e4d',
 'tools/cnc-kernel/kernel-face.hpp': '100644 blob 0f3eba8ca3fe17105c467cbde524f7460ad2a503',
 'tools/cnc-kernel/kernel-native-interpretation-audit.hpp': '100644 blob 8424c44fd776601a63cc06a9c88e7e93a86eb62e',
 'tools/cnc-kernel/kernel-native-interpretation-checks.hpp': '100644 blob 5b6b530c83cca3edb5438a471cdb2075d37e4e6c',
 'tools/cnc-kernel/kernel-native-interpretation-export.hpp': '100644 blob 9f2817aa39a82823d2c0702b367da0f607e82915',
 'tools/cnc-kernel/kernel-native-interpretation-ledger.hpp': '100644 blob 2667d8b1a3a2886e2cf2bdacb8a4eb8f64b8c1d4',
 'tools/cnc-kernel/kernel-native-warning-lineage.hpp': '100644 blob 4b222322bbb8f5331ae4a5eb8d80fa1c61f0b981',
 'tools/cnc-kernel/kernel-region-measures.hpp': '100644 blob b97bcd9e935b909067a8025ea2eb0ad3722fd106',
 'tools/cnc-kernel/kernel-repair-bounds.hpp': '100644 blob 31d6cbb39c753aa64292acebd05748214bcf10a1',
 'tools/cnc-kernel/kernel-repair-composition.hpp': '100644 blob fa0e23d25649b27c1fa0ee08a4dcd074da96bea2',
 'tools/cnc-kernel/kernel-repair-cone-region.hpp': '100644 blob 66a3460c49523b30f5c6c0c925d585d95d61ca99',
 'tools/cnc-kernel/kernel-repair-correlated.hpp': '100644 blob 299995c77928c8417612e53b10d5d49df8a64088',
 'tools/cnc-kernel/kernel-repair-cylinder-composition.hpp': '100644 blob bd57e9e51f585fe74c5c032bb033608579e44636',
 'tools/cnc-kernel/kernel-repair-domains.hpp': '100644 blob d7ccf79aaef3ba057e99b2014759ee7f1c27f347',
 'tools/cnc-kernel/kernel-repair-endpoints.hpp': '100644 blob 55a1e9f1cde7b929eeb2a62effe288919378923e',
 'tools/cnc-kernel/kernel-repair-export.hpp': '100644 blob 48a033e1aeb69db5755f5a4d48b8b1ba507acd2a',
 'tools/cnc-kernel/kernel-repair-high-axis-composition.hpp': '100644 blob 9d743dfc37bbefb7b6b71dbb8702d21b78713c2a',
 'tools/cnc-kernel/kernel-repair-policy.hpp': '100644 blob 5e383033e817e3fb7de83f002e7a06bee84ae915',
 'tools/cnc-kernel/kernel-repair-rational-composition.hpp': '100644 blob e4141b0ab4a1fcf2882af43383e8d6de9fa9b84d',
 'tools/cnc-kernel/kernel-repair-residual.hpp': '100644 blob 2a6988975e0c1858d6b5c9dd6e982b635f444023',
 'tools/cnc-kernel/kernel-repair.hpp': '100644 blob 9a417493bf1e906c4c8766c539e1f7290b3cfc13',
 'tools/cnc-kernel/kernel-rotational-band.hpp': '100644 blob 8367af577051c7d37de982cf7795d254f75c0d5f',
 'tools/cnc-kernel/kernel-stock-frames.hpp': '100644 blob 2068388b0b1d055e298dd6e85f940766683a8c4a',
 'tools/cnc-kernel/kernel-target-query-profile.hpp': '100644 blob 50ed5ca8407285d464c6bc0ac93c00c9452b4fc7',
 'tools/cnc-kernel/kernel-target-query-session.hpp': '100644 blob a6c32d67bd1732b69560dc4b6c4e45c72a4cc870',
 'tools/cnc-kernel/kernel-target-query.hpp': '100644 blob 33e44848e885bede069df7b8bb54db65bdcf14bf',
 'tools/cnc-kernel/kernel-transfer.hpp': '100644 blob c8105aa011c189f26c9ddb36ad994044c3ada262',
 'tools/cnc-kernel/kernel-triangulation-correspondence.hpp': '100644 blob 49d3a61e2d95d984d92616123db7286eaada09ab',
 'tools/cnc-kernel/kernel-trims.hpp': '100644 blob b364b3950dc90fdb8abda839664c757747a07e8a',
 'tools/cnc-kernel/test-affine-pcurve-band-native.cpp': '100644 blob 38d517fed1ae5133cd3610dd8d05886a8d50519a',
 'tools/cnc-kernel/test-bodies.cjs': '100644 blob e404670018a2f5e8c570b3950626f889a33416bb',
 'tools/cnc-kernel/test-circular-revolution-native.cpp': '100644 blob f47af8c413c1a31ee3c64ff63ed4a971b21941a0',
 'tools/cnc-kernel/test-document.cjs': '100644 blob e028f71559b53365d0a18b3f15cb11310162e609',
 'tools/cnc-kernel/test-edge-membership-context-native.cpp': '100644 blob 519df4a60e11053b70d929d86478d96ed9768684',
 'tools/cnc-kernel/test-export.cjs': '100644 blob 53704a55238af3417e2ef5cf56ff0f84f4dba76a',
 'tools/cnc-kernel/test-native-connector-proof.cpp': '100644 blob 3677919a2bc0075f084b22fa0349e4fe4b65ce99',
 'tools/cnc-kernel/test-native-interpretation-checks.cpp': '100644 blob 1730adaf17e30d7862fe85d31bf826fc30e56bef',
 'tools/cnc-kernel/test-native-interpretation-ledger.cpp': '100644 blob f193557868ceb0cf7507f5a1ee776ab2c8aded8a',
 'tools/cnc-kernel/test-native-interpretation-periodic.cpp': '100644 blob 0a41e1f1c24d4e7587cf1ad3715662e88b4d3f64',
 'tools/cnc-kernel/test-native-interpretation-producer.cpp': '100644 blob e0a0e6633585ff12127a2dd71db8361a81b50b6b',
 'tools/cnc-kernel/test-native-interpretation.cjs': '100644 blob 2fa10f530c762cff3dadad8949b092877502dcbb',
 'tools/cnc-kernel/test-native-warning-lineage.cpp': '100644 blob e798122fa28ab77aee01771f95011789468d9494',
 'tools/cnc-kernel/test-region-measures-native.cpp': '100644 blob 2d285a362a6dd7ad010ab64093c80b31eb7137b2',
 'tools/cnc-kernel/test-region-measures.cjs': '100644 blob 6a972d5430f0c8c4fecc0b2154a61a139b0e227c',
 'tools/cnc-kernel/test-region-overlay-wiring.cjs': '100644 blob f79f0acad8d99b89d77481f862dc890f79e91584',
 'tools/cnc-kernel/test-repair-composition-native.cpp': '100644 blob 8ef3d6b8c6c211c681fced06b42d36636f289d47',
 'tools/cnc-kernel/test-repair-cone-region-native.cpp': '100644 blob ba01746a399362de09833906b3dcfd8db0f3af5b',
 'tools/cnc-kernel/test-repair-correlated-native.cpp': '100644 blob bde36a23c173b99421bf1a90d8da0fea1405237f',
 'tools/cnc-kernel/test-repair-coverage-native.cpp': '100644 blob 17c44a98086f85fc0d2197123d771fce283d9b55',
 'tools/cnc-kernel/test-repair-cylinder-composition-native.cpp': '100644 blob 5dee72713ac87e9873b5c58a84a03f4accfb616e',
 'tools/cnc-kernel/test-repair-cylinder-dispatch-native.cpp': '100644 blob 1ea627e01a2fc6579cc5c0349647710e940f6429',
 'tools/cnc-kernel/test-repair-degenerate-native.cpp': '100644 blob 759c9ce6794b0a25fc584930dbb08d7717e4c381',
 'tools/cnc-kernel/test-repair-domains-native.cpp': '100644 blob 435c457489b12ec1afa4c33fcdf5796e2977b220',
 'tools/cnc-kernel/test-repair-endpoints-native.cpp': '100644 blob 6de1674df414f58ea7fe68b665ce2b0a697908dc',
 'tools/cnc-kernel/test-repair-high-axis-composition-native.cpp': '100644 blob d8e9577858e6eb93490a89365d75144e7160d427',
 'tools/cnc-kernel/test-repair-high-axis-dispatch-native.cpp': '100644 blob 123e3e935f115cb364f1c47c87d0c5e6bb902db3',
 'tools/cnc-kernel/test-repair-metadata-native.cpp': '100644 blob 3f11dfb9cbdd24e6f9755f2e2d9398b84df690d6',
 'tools/cnc-kernel/test-repair-native.cpp': '100644 blob 0895d1e338eee517650a487843fb5a1147058c1a',
 'tools/cnc-kernel/test-repair-overlay-wiring.cjs': '100644 blob a383e6f3cfed8f2fe3da688843ff23be2ac94526',
 'tools/cnc-kernel/test-repair-policy-native.cpp': '100644 blob 2e999fc25180399c812ef68932fd519b922a5431',
 'tools/cnc-kernel/test-repair-rational-composition-native.cpp': '100644 blob 84cbc7ff8989158414c827747fccff8b2930f6dc',
 'tools/cnc-kernel/test-repair-rational-dispatch-native.cpp': '100644 blob 9bef9a903e97403df646ca2e9b408f6589e664cc',
 'tools/cnc-kernel/test-repair-thread-transform-native.cpp': '100644 blob bda82b937aef952208d029b259a01b9651b20c6b',
 'tools/cnc-kernel/test-repair-trig-native.cpp': '100644 blob dc40c0a306b28617e1439fac48e1a59b6b9c9c62',
 'tools/cnc-kernel/test-repair.cjs': '100644 blob 6c00d9be5bd8ad84e455c5a58fdb64c46a9945e4',
 'tools/cnc-kernel/test-rotational-band-native.cpp': '100644 blob b0784340963a80e26348ef57e19a19d2d5a704db',
 'tools/cnc-kernel/test-rotational-band.cjs': '100644 blob 8d108102d52abb2dda56212b63206120aa940d5a',
 'tools/cnc-kernel/test-source-declared-cycle-native.cpp': '100644 blob 2077328f571f4385263f883668ab7f7da8957373',
 'tools/cnc-kernel/test-source-declared-cycle-native.sh': '100644 blob 04afc2d1bab2fdfb18557a5b807411442c4ccf31',
 'tools/cnc-kernel/test-source-declared-cycle.cjs': '100644 blob ab5f11044f39f0552f74427a849722e1d114e2b7',
 'tools/cnc-kernel/test-stock-validation-native.cpp': '100644 blob dfa1a8279e90ccd7db528e1a3ceabe3ad58460de',
 'tools/cnc-kernel/test-target-query-native.cpp': '100644 blob bdd0d342718903d470c8eaa40b0cc54d606433fa',
 'tools/cnc-kernel/test-target-query-native.sh': '100644 blob b85474bc63c850ed2bd50ce6de484ea036958514',
 'tools/cnc-kernel/test-target-query-overlay.cjs': '100644 blob 0ed6c0c83e46c92900d81d2802c7214342e9dcbf',
 'tools/cnc-kernel/test-target-query-profile-overlay.cjs': '100644 blob 25b3d5071aa5fb86237e92fd1e02b057001462fe',
 'tools/cnc-kernel/test-target-query-profile.cjs': '100644 blob ef80c3ac55a7985ec4d14f2a5241e7a1178797c6',
 'tools/cnc-kernel/test-target-query-profile.cpp': '100644 blob db394bab817ed88c2f3363529e618771a8142d7d',
 'tools/cnc-kernel/test-target-query.cjs': '100644 blob 0e5948912724d12f9a299c28de9154dc151966d4',
 'tools/cnc-kernel/test-thread-transform-native.sh': '100644 blob 4f6c95f6aa6e63d703def403c8ab410ff127b87c',
 'tools/cnc-kernel/test-trims.cjs': '100644 blob 0df169d989e7fdd001662fa32d2ad2dfec68e26b',
 'tools/cnc-lease-probe/CncLeaseProbe.csproj': '100644 blob dab6122f1e5f0201236fb25fb99aadc51c3f9606',
 'tools/cnc-lease-probe/Program.cs': '100644 blob 6074343cabca6b8bb2b1e96a0027409366c1d72a',
 'tools/fx-auth-host/FxAuthHost.csproj': '100644 blob f0036d988ea83931b1412bd6370d85742b52ebbb',
 'tools/fx-auth-host/NativeAuthHost.cs': '100644 blob 5d94bd79adccea66ff0f0f0aae4c6873106fc981',
 'tools/fx-catalog-host/FxCatalogHost.csproj': '100644 blob 796015368b6b106f0eb1db24a762f29b211225b3',
 'tools/fx-catalog-host/NativeCatalogHost.cs': '100644 blob 92674eed532da622bfce7e33b640788afad8d49b',
 'tools/fx-provider-transport/FxProviderTransport.csproj': '100644 blob 0bfa6d0880542ed1ea1795fe92f8e8f56a4b379b',
 'tools/fx-provider-transport/ProviderTransportStartup.cs': '100644 blob 2a9794a12c3f436a2204e5648426444286a1089e',
 'tools/profile-auth-seed/ProfileAuthSeed.csproj': '100644 blob e9b2a524a4b75ac85ad78ef6e8e198cbd4b42015',
 'tools/profile-auth-seed/Program.cs': '100644 blob 4e53fd2e99f0a76270b0a2bfe4bf14c1a8e1341e',
 'tools/profile-contract-seed/ProfileContractSeed.csproj': '100644 blob 6fe0ea88b1d61614f93b78531ed328c6c941cd17',
 'tools/profile-contract-seed/Program.cs': '100644 blob b9e0545a425111775ef1b88db1e8791fdb90803a'}
FIXED_TRANSFORMS={'.github/workflows/member-company-catalog-persistence.yml': [{'after': '      - name: Authenticate exact current Company source before SDK or '
                                                                        'backend allocation\n'
                                                                        '        timeout-minutes: 1\n'
                                                                        '        run: python3 -B '
                                                                        'acceptance/MemberCompanyCatalogPersistence/current_consumer.py\n',
                                                               'before': '',
                                                               'oldLineCount': 0,
                                                               'startLine': 43},
                                                              {'after': '        run: |\n'
                                                                        '          set -euo pipefail\n'
                                                                        '          GITHUB_ACTIONS=false DOTNET_CLI_UI_LANGUAGE=en-US dotnet build '
                                                                        'Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --nologo '
                                                                        '| tee member-company-build-web-tests.log\n',
                                                               'before': '        run: GITHUB_ACTIONS=false dotnet build '
                                                                         'Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release '
                                                                         '--nologo\n',
                                                               'oldLineCount': 1,
                                                               'startLine': 76},
                                                              {'after': '        run: |\n'
                                                                        '          set -euo pipefail\n'
                                                                        '          GITHUB_ACTIONS=false DOTNET_CLI_UI_LANGUAGE=en-US dotnet build '
                                                                        'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistence.Tests.csproj '
                                                                        '-c Release --nologo | tee member-company-build-company-tests.log\n'
                                                                        '      - name: Verify bounded runtime-probe ownership controls\n'
                                                                        '        timeout-minutes: 1\n'
                                                                        '        run: |\n'
                                                                        '          python3 -B '
                                                                        'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_all_controls.py\n'
                                                                        '          python3 -B -O '
                                                                        'acceptance/MemberCompanyCatalogPersistence/runtime_probe/test_all_controls.py\n'
                                                                        '      - name: Observe actual test-host assembly identities without a '
                                                                        'backend\n'
                                                                        '        timeout-minutes: 3\n'
                                                                        '        run: python3 -B '
                                                                        'acceptance/MemberCompanyCatalogPersistence/runtime_probe/run_hosted.py\n'
                                                                        '      - name: Bind actual current build source and package DLLs before any '
                                                                        'journey backend\n'
                                                                        '        timeout-minutes: 1\n'
                                                                        '        run: python3 -B '
                                                                        'acceptance/MemberCompanyCatalogPersistence/capture_build_source.py\n'
                                                                        '      - name: Verify passive linked-instance observation controls without '
                                                                        'any backend\n'
                                                                        '        timeout-minutes: 1\n'
                                                                        '        run: |\n'
                                                                        '          dotnet test '
                                                                        'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistence.Tests.csproj '
                                                                        '-c Release --no-build --no-restore -p:CI=false -p:VSTestCollect= '
                                                                        '-p:RunSettingsFilePath= --filter '
                                                                        '"FullyQualifiedName~CurrentCompanySourceObservationTests" --logger trx '
                                                                        '--results-directory ./member-company-observation-controls\n'
                                                                        '          python3 -B '
                                                                        'acceptance/MemberCompanyCatalogPersistence/current_consumer.py '
                                                                        '--verify-observation-controls\n',
                                                               'before': '        run: GITHUB_ACTIONS=false dotnet build '
                                                                         'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistence.Tests.csproj '
                                                                         '-c Release --nologo\n',
                                                               'oldLineCount': 1,
                                                               'startLine': 78},
                                                              {'after': '      - name: Require original31 plus accepted19 exact current lifecycle '
                                                                        'identities\n'
                                                                        '        run: >-\n'
                                                                        '          python3 -B scripts/verify-focused-web-trx.py\n'
                                                                        '          --directory billing-lifetime-test-results --suite '
                                                                        'billing-lifetime\n'
                                                                        '          --assembly '
                                                                        'Legacy.Maliev.Web.Tests/bin/Release/net10.0/Legacy.Maliev.Web.Tests.dll\n',
                                                               'before': '      - name: Require all thirty-one exact unique passed lifecycle control '
                                                                         'rows\n'
                                                                         '        run: |\n'
                                                                         "          python3 -B - <<'PY'\n"
                                                                         '          from pathlib import Path\n'
                                                                         '          import xml.etree.ElementTree as ET\n'
                                                                         "          ns = {'t': "
                                                                         "'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}\n"
                                                                         '          expected = {\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.Expiry_JoinsAdmittedSynchronousBodyBeforeDependencyRelease',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.Expiry_JoinsAllocationReservationAndReleasesLateResourceExactlyOnce(dependency: "
                                                                         "False)',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.Expiry_JoinsAllocationReservationAndReleasesLateResourceExactlyOnce(dependency: "
                                                                         "True)',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ProvenNoLiveChild_ClosesOriginalWrapperWithoutLiveExecutableLookup(state: "
                                                                         '"exited")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ProvenNoLiveChild_ClosesOriginalWrapperWithoutLiveExecutableLookup(state: "
                                                                         '"refused")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ActualIdleExpiry_ClosesOriginalChildBeforeDependency_AndReleasesOwner',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ActualExpiry_DelayedFrontendShutdownRetainsBackendAndCaseWorkUntilSettled',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.AcquisitionFault_AfterAllocationRetainsOriginalChildForCleanup(stage: "
                                                                         '"start")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.AcquisitionFault_AfterAllocationRetainsOriginalChildForCleanup(stage: "
                                                                         '"identity")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.AcquisitionFault_AfterAllocationRetainsOriginalChildForCleanup(stage: "
                                                                         '"readers")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedCleanup_RetainsOwnershipAndDependencies_ThenExactRetryCanRelease(stage: "
                                                                         '"kill")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedCleanup_RetainsOwnershipAndDependencies_ThenExactRetryCanRelease(stage: "
                                                                         '"reap")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedCleanup_RetainsOwnershipAndDependencies_ThenExactRetryCanRelease(stage: "
                                                                         '"readers")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedCleanup_RetainsOwnershipAndDependencies_ThenExactRetryCanRelease(stage: "
                                                                         '"reader-close")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedCleanup_RetainsOwnershipAndDependencies_ThenExactRetryCanRelease(stage: "
                                                                         '"handle-close")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedHostDisposal_IsStickyAndCannotBecomeNoOpClearance',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.InterruptedHostStartup_RefusesHostDisposalAndDependencyRelease',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.SettledChildReaders_BeforeHandleAndDependencyRelease_AllHostsAttemptedFirst',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.SynchronousBlockedHostRelease_IsBoundedRetainedAndReusesOriginalAttempt',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.SettledReaderFault_ReleasesResourcesButRepeatDisposalStillRejectsProof',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: "
                                                                         '"daemon")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: "
                                                                         '"id")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: "
                                                                         '"created")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: "
                                                                         '"owner")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: "
                                                                         '"envelope")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: "
                                                                         '"init")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: "
                                                                         '"signature")\',\n'
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendExactOriginalRemoval_IsReobservedAfterSdkDisposal',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendSdkCreationIdUnavailable_RefusesNameBasedAdoptionAndCleanup',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendSdkIdentityLostBeforeDisposal_RefusesMutation',\n"
                                                                         '              '
                                                                         "'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendUnsettledRemoval_RefusesAbsenceAndSdkRelease',\n"
                                                                         '          }\n'
                                                                         '          files = '
                                                                         "list(Path('billing-lifetime-test-results').glob('*.trx'))\n"
                                                                         '          if len(files) != 1:\n'
                                                                         "              raise SystemExit('Exactly one lifecycle TRX required')\n"
                                                                         '          root = ET.parse(files[0]).getroot()\n'
                                                                         "          rows = root.findall('.//t:UnitTestResult', ns)\n"
                                                                         "          names = [row.get('testName') for row in rows]\n"
                                                                         '          if len(rows) != 31 or len(set(names)) != 31 or set(names) != '
                                                                         "expected or any(row.get('outcome') != 'Passed' for row in rows):\n"
                                                                         "              raise SystemExit('Missing, duplicate, foreign or "
                                                                         "unsuccessful lifecycle control row')\n"
                                                                         "          counters = root.find('.//t:Counters', ns)\n"
                                                                         "          required = {'total': '31', 'executed': '31', 'passed': '31', "
                                                                         "'failed': '0', 'error': '0', 'timeout': '0',\n"
                                                                         "              'aborted': '0', 'inconclusive': '0', 'passedButRunAborted': "
                                                                         "'0', 'notRunnable': '0', 'notExecuted': '0',\n"
                                                                         "              'disconnected': '0', 'warning': '0', 'completed': '0', "
                                                                         "'inProgress': '0', 'pending': '0'}\n"
                                                                         '          if counters is None or any(counters.get(key) != value for key, '
                                                                         'value in required.items()):\n'
                                                                         "              raise SystemExit('Lifecycle TRX counters do not match "
                                                                         "thirty-one completed Passed rows')\n"
                                                                         "          print('Verified thirty-one exact unique Passed lifecycle "
                                                                         "controls; no skipped or unsuccessful rows')\n"
                                                                         '          PY\n',
                                                               'oldLineCount': 55,
                                                               'startLine': 186},
                                                              {'after': '        run: python3 -B '
                                                                        'acceptance/MemberCompanyCatalogPersistence/test_current_consumer.py '
                                                                        '--historical\n',
                                                               'before': '        run: python3 -B '
                                                                         'acceptance/MemberCompanyCatalogPersistence/test_retain.py\n',
                                                               'oldLineCount': 1,
                                                               'startLine': 246}],
 'acceptance/MemberCompanyCatalogPersistence/README.md': [{'after': '\n'
                                                                    '## Current dependency consumer (unpublished)\n'
                                                                    '\n'
                                                                    'The proposed current consumer keeps the accepted723 lifetime helper and all '
                                                                    'original journey/parent-test bodies unchanged. It authenticates exact '
                                                                    'working/HEAD source before SDK or backend work, aligns the Company Test SDK '
                                                                    'with Web.Tests18.11.0, and validates the current released cleanup stage before '
                                                                    'invoking unchanged historical receipt rules on an explicit temporary '
                                                                    'projection. Current raw cleanup hashes and current source provenance are '
                                                                    'retained; PR527 helper assertions remain historical controls against exact '
                                                                    'predecessor bytes.\n'
                                                                    '\n'
                                                                    'The passive observer reads actual loaded assembly revisions, MVIDs and DLL '
                                                                    'hashes and checks the same two linked handler instances. It preserves '
                                                                    'separately read -standard/catalog-standard max-three options and labels key '
                                                                    'association as inferred from immutable registration source. Same-workflow build '
                                                                    'evidence joins actual dependency HEADs, generated Defaults metadata, resolved '
                                                                    'official package SHA512/source and built DLLs. An unreviewed actual Http/Polly '
                                                                    'revision fails closed with bounded evidence; package minimums are never '
                                                                    'reported as actual runtime selection.\n'
                                                                    '\n'
                                                                    'The current lifecycle gate retains all original31 identities and the19 already '
                                                                    'accepted additions, requiring50 exact Passed rows. Six separate owned in-memory '
                                                                    'observation controls cover linked order/refusals without a socket/backend. '
                                                                    'Canonical72 retainer controls remain unchanged and execute only against an '
                                                                    'authenticated historical fixture; distinct current controls exercise current '
                                                                    'sources and receipts. None of these source controls establishes native Company, '
                                                                    'FX, source closure, genuine IAM, live-provider, deployment or financial '
                                                                    'acceptance. All existing hosted exact-head and protected-main gates remain '
                                                                    'required.\n'
                                                                    '\n'
                                                                    'The original design/provenance below describes the historical predecessor. Its '
                                                                    'older helper/package identity and31-row statements do not authorize current '
                                                                    'acceptance.\n',
                                                           'before': '',
                                                           'oldLineCount': 0,
                                                           'startLine': 2}],
 'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistence.Tests.csproj': [{'after': '    <PackageReference '
                                                                                                             'Include="Microsoft.NET.Test.Sdk" '
                                                                                                             'Version="18.11.0" />\n',
                                                                                                    'before': '    <PackageReference '
                                                                                                              'Include="Microsoft.NET.Test.Sdk" '
                                                                                                              'Version="18.8.1" />\n',
                                                                                                    'oldLineCount': 1,
                                                                                                    'startLine': 8}],
 'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistenceTests.cs': [{'after': '    private readonly List<(string Name, int '
                                                                                                        'Count, int OuterMaxRetries, int '
                                                                                                        'InnerMaxRetries, string PackageRevision, '
                                                                                                        'CurrentCompanySourceObservation Evidence)> '
                                                                                                        'samples = [];\n',
                                                                                               'before': '    private readonly List<(string Name, '
                                                                                                         'int Count, int OuterMaxRetries, int '
                                                                                                         'InnerMaxRetries, string PackageRevision)> '
                                                                                                         'samples = [];\n',
                                                                                               'oldLineCount': 1,
                                                                                               'startLine': 336},
                                                                                              {'after': '        // These remain separate actual '
                                                                                                        'options reads. Pipeline key association is '
                                                                                                        'source-inferred.\n',
                                                                                               'before': '        // Pinned runtime default '
                                                                                                         'builder.Name=null derives -standard; named '
                                                                                                         'Catalog derives catalog-standard.\n'
                                                                                                         '        // These are separate actual '
                                                                                                         'options reads, not a private '
                                                                                                         'handler-to-options inspection.\n',
                                                                                               'oldLineCount': 2,
                                                                                               'startLine': 342},
                                                                                              {'after': '        var evidence = '
                                                                                                        'CurrentCompanySourceObservation.Capture(builder);\n'
                                                                                                        '        lock (gate) '
                                                                                                        'samples.Add((builder.Name, count, '
                                                                                                        'outerMaxRetries, innerMaxRetries, '
                                                                                                        'evidence.PackageRevision, evidence));\n',
                                                                                               'before': '        var version = '
                                                                                                         'typeof(ResilienceHandler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;\n'
                                                                                                         '        var packageRevision = '
                                                                                                         "version?.Split('+').ElementAtOrDefault(1) "
                                                                                                         '?? string.Empty;\n'
                                                                                                         '        lock (gate) '
                                                                                                         'samples.Add((builder.Name, count, '
                                                                                                         'outerMaxRetries, innerMaxRetries, '
                                                                                                         'packageRevision));\n',
                                                                                               'oldLineCount': 3,
                                                                                               'startLine': 347},
                                                                                              {'after': '            Assert.All(samples, sample =>\n'
                                                                                                        '            {\n'
                                                                                                        '                Assert.Equal((first.Name, '
                                                                                                        'first.Count, first.OuterMaxRetries, '
                                                                                                        'first.InnerMaxRetries, '
                                                                                                        'first.PackageRevision),\n'
                                                                                                        '                    (sample.Name, '
                                                                                                        'sample.Count, sample.OuterMaxRetries, '
                                                                                                        'sample.InnerMaxRetries, '
                                                                                                        'sample.PackageRevision));\n'
                                                                                                        '                '
                                                                                                        'Assert.True(first.Evidence.Equivalent(sample.Evidence));\n'
                                                                                                        '                '
                                                                                                        'sample.Evidence.VerifyLinkedOrder();\n'
                                                                                                        '            });\n',
                                                                                               'before': '            Assert.All(samples, sample => '
                                                                                                         'Assert.Equal(first, sample));\n',
                                                                                               'oldLineCount': 1,
                                                                                               'startLine': 357},
                                                                                              {'after': '                && first.PackageRevision == '
                                                                                                        '"25f07afa51034e37b334210642d7dc5f191c142d";\n',
                                                                                               'before': '                && first.PackageRevision '
                                                                                                         '== '
                                                                                                         '"02107c65bab30aad9e35b5133ed643eaa77bccd8";\n',
                                                                                               'oldLineCount': 1,
                                                                                               'startLine': 359},
                                                                                              {'after': '                runtimeDefaultsRevision = '
                                                                                                        'first.Evidence.DefaultsRevision,\n',
                                                                                               'before': '                runtimeDefaultsRevision = '
                                                                                                         '"3c790ba6414b2a539f24aabb6948549ffd81a86b",\n',
                                                                                               'oldLineCount': 1,
                                                                                               'startLine': 364},
                                                                                              {'after': '                assemblyEvidence = '
                                                                                                        'first.Evidence.AssemblyReceipt(),\n'
                                                                                                        '                linkedHandlerOrder = new[] '
                                                                                                        '{ 0, 1 },\n'
                                                                                                        '                linkedHandlerOrderVerified '
                                                                                                        '= true,\n'
                                                                                                        '                buildEvidenceSha256 = '
                                                                                                        'first.Evidence.BuildEvidenceSha256,\n'
                                                                                                        '                sourceProfileSha256 = '
                                                                                                        'first.Evidence.SourceProfileSha256,\n',
                                                                                               'before': '',
                                                                                               'oldLineCount': 0,
                                                                                               'startLine': 374},
                                                                                              {'after': '',
                                                                                               'before': '\n',
                                                                                               'oldLineCount': 1,
                                                                                               'startLine': 378}],
 'acceptance/MemberCompanyCatalogPersistence/retain.py': [{'after': 'import sys\nimport current_consumer as current\n',
                                                           'before': '',
                                                           'oldLineCount': 0,
                                                           'startLine': 9},
                                                          {'after': '    current.authenticate_sources()\n'
                                                                    '    helper_digest = '
                                                                    "current.current_helper_source(Path('Legacy.Maliev.Web.Tests/BillingProofLifetime.cs'))\n"
                                                                    '    build_evidence, build_evidence_sha = current.load_build_evidence()\n'
                                                                    '    observation_controls = current.verify_observation_controls()\n',
                                                           'before': '    helper_digest = '
                                                                     "helper_source(Path('Legacy.Maliev.Web.Tests/BillingProofLifetime.cs'))\n",
                                                           'oldLineCount': 1,
                                                           'startLine': 139},
                                                          {'after': '        case = '
                                                                    "current.current_receipt(current.parse((source/(culture+'.json')).read_bytes()), "
                                                                    'culture, candidate, build_evidence, build_evidence_sha, '
                                                                    'sys.modules[__name__])\n',
                                                           'before': "        case = receipt(json.loads((source/(culture+'.json')).read_text()), "
                                                                     'culture, candidate)\n',
                                                           'oldLineCount': 1,
                                                           'startLine': 147},
                                                          {'after': '        released = current.current_cleanup(current.parse(raw_cleanup), '
                                                                    'sys.modules[__name__])\n',
                                                           'before': '        released = cleanup(json.loads(raw_cleanup))\n',
                                                           'oldLineCount': 1,
                                                           'startLine': 150},
                                                          {'after': '    '
                                                                    "(output/'source-provenance.json').write_text(json.dumps({'candidateHead':candidate,'sourceSha256':sources,'lifetimeHelperRevision':'a25cf00409b9df2c7164c24f0a399b0752242e2d','historicalLifetimeHelperRevision':HELPER_REVISION,'currentProfileSha256':build_evidence['profileSha256'],'buildEvidenceSha256':build_evidence_sha,'observationControls':observation_controls,'webRuntimeSourceRevisions':build_evidence['sourceRevisions'],'rawCleanupSha256':raw_cleanup_hashes,'publicServiceGitRevisions':service_git_revisions,'sharedAuthorityLifecycleQualified':False,'wholeAppHostQualified':False,'genuineIamEnrollmentQualified':False,'liveProviderQualified':False},indent=2)+'\\n')\n",
                                                           'before': '    '
                                                                     "(output/'source-provenance.json').write_text(json.dumps({'candidateHead':candidate,'sourceSha256':sources,'lifetimeHelperRevision':HELPER_REVISION,'rawCleanupSha256':raw_cleanup_hashes,'publicServiceGitRevisions':service_git_revisions,'sharedAuthorityLifecycleQualified':False,'wholeAppHostQualified':False,'genuineIamEnrollmentQualified':False,'liveProviderQualified':False},indent=2)+'\\n')\n",
                                                           'oldLineCount': 1,
                                                           'startLine': 183}]}
ARCHIVES={'.github/workflows/member-company-catalog-persistence.yml': {'identity': {'bytes': 18505,
                                                                           'gitBlob': 'f3a653f8c6653a476433d8a7cb4956d55ee58b21',
                                                                           'sha256': 'eb3390ae61f09116253210c7e337c2d366ebc97011d8d83dd260a98b10157516'},
                                                              'path': 'acceptance/MemberCompanyCatalogPersistence/predecessors/before-current-0.bin'},
 'acceptance/MemberCompanyCatalogPersistence/README.md': {'identity': {'bytes': 8679,
                                                                       'gitBlob': '475d88327eb3caf0784c71b604796c2473a3dbc9',
                                                                       'sha256': '80705a486116ddff2f571526aaefbbb3bc14f5e0d26339faae7e4620f1306638'},
                                                          'path': 'acceptance/MemberCompanyCatalogPersistence/predecessors/before-current-4.bin'},
 'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistence.Tests.csproj': {'identity': {'bytes': 698,
                                                                                                                'gitBlob': '4a16afea49d08f32d019a23e0766580024d40343',
                                                                                                                'sha256': '365b519a6b46d806a91f62d9c92314d5c053cec30b67c1a46289da1140d9b0a7'},
                                                                                                   'path': 'acceptance/MemberCompanyCatalogPersistence/predecessors/before-current-1.bin'},
 'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistenceTests.cs': {'identity': {'bytes': 32564,
                                                                                                           'gitBlob': 'e4b813901dbe30f48a5625dd69fa90aa0a33222b',
                                                                                                           'sha256': '64ab3ce503697cdb04aa5c5d8e50504d67bb1d17b010295b833267647f521fa7'},
                                                                                              'path': 'acceptance/MemberCompanyCatalogPersistence/predecessors/before-current-2.bin'},
 'acceptance/MemberCompanyCatalogPersistence/retain.py': {'identity': {'bytes': 19796,
                                                                       'gitBlob': '2e4c024e1562706ad43374cbdec5222c62b7c8f9',
                                                                       'sha256': 'f06dbcebf9cb462764db40c5462c70e6fafb3be2b794017b9d43a1c4c7040d6c'},
                                                          'path': 'acceptance/MemberCompanyCatalogPersistence/predecessors/before-current-3.bin'},
 'historical-pr527-helper': {'identity': {'bytes': 46531,
                                          'gitBlob': 'f350d0fdd6224d6ca4d8745b272de85a41c7f45a',
                                          'sha256': '096be55636a60184c452ab0c85a562d0b4b0926e310e95c0a6cf841f00bfccd4'},
                             'path': 'acceptance/MemberCompanyCatalogPersistence/predecessors/helper-pr527.cs.bin'}}
# END GENERATED CLOSED SOURCE

def need(value,message):
    if not value: raise ValueError('Current Company: '+message)

def same(a,b):
    return json.dumps(a,sort_keys=True,separators=(',',':'),allow_nan=False)==json.dumps(b,sort_keys=True,separators=(',',':'),allow_nan=False)

def identity(raw):
    return dict(bytes=len(raw),sha256=hashlib.sha256(raw).hexdigest(),gitBlob=hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest())

def read(root,name,cap=1048576):
    need(isinstance(name,str) and '\\' not in name and ':' not in name and all(x not in ('','.','..') for x in name.split('/')),'safe relative source path')
    root=Path(root);path=root/name
    for node in (path,*path.parents):
        if node==root.parent:break
        need(not node.is_symlink() and not (hasattr(node,'is_junction') and node.is_junction()),'source link refused')
    before=path.lstat();key=lambda s:(s.st_dev,s.st_ino,s.st_size,s.st_mtime_ns)
    need(stat.S_ISREG(before.st_mode) and before.st_nlink==1 and 0<before.st_size<=cap,'bounded regular input')
    with path.open('rb') as stream:
        need(key(os.fstat(stream.fileno()))==key(before),'input replaced before read')
        raw=stream.read(cap+1)
        need(key(os.fstat(stream.fileno()))==key(before),'input changed during read')
    need(len(raw)==before.st_size and key(path.lstat())==key(before),'input replaced after read')
    return raw

LOADED_MODULE_BYTES=read(ROOT,MODULE)

def parse(raw):
    def unique(items):
        result={}
        for key,value in items:
            need(key not in result,'duplicate JSON field');result[key]=value
        return result
    return json.loads(raw,object_pairs_hook=unique,parse_constant=lambda x:need(False,'nonfinite JSON number'))

def expected_profile(root):
    return dict(schemaVersion=1,profile='company-current-helper-dependency-source-v1',predecessorMain='e77fdd2db35cb31e4a354d76036bc51524050337',acceptedIntegrationMain='547bdac1a0bdedc5b9204ff143c11e7acc275600',retainedPrHead='722f701d696e5db885cae1b602651d07d465c1ea',
        sourceFiles=EXPECTED_FILES,verifier=dict(path=MODULE,**identity(read(root,MODULE))),fixedTransforms=FIXED_TRANSFORMS,predecessorArchives=ARCHIVES,
        executionGranted=False,sdkGranted=False,runtimeQualified=False,financialPhaseGranted=False)

def authenticate_bytes(root):
    need(read(root,MODULE)==LOADED_MODULE_BYTES,'loaded current verifier changed')
    for name,row in EXPECTED_FILES.items():need(same(identity(read(root,name)),row),'fixed current source differs: '+name)
    for name,row in ARCHIVES.items():
        raw=read(root,row['path']);need(same(identity(raw),row['identity']),'immutable predecessor differs: '+name)
        if name in FIXED_TRANSFORMS:
            lines=raw.splitlines(keepends=True)
            for edit in reversed(FIXED_TRANSFORMS[name]):
                at=edit['startLine']-1
                need(b''.join(lines[at:at+edit['oldLineCount']])==edit['before'].encode(),'fixed transform preimage')
                lines[at:at+edit['oldLineCount']]=[edit['after'].encode()]
            need(b''.join(lines)==read(root,name),'fixed transform postimage')
    profile=parse(read(root,PROFILE));need(same(profile,expected_profile(root)),'closed source profile/false grants')
    return profile

def git(root,*args):
    with __import__('tempfile').TemporaryFile() as output:
        result=subprocess.run(['git','-C',str(root),*args],stdout=output,stderr=subprocess.PIPE,timeout=15)
        need(result.returncode==0,'Git metadata failed: '+result.stderr.decode(errors='replace')[:1000])
        output.seek(0);raw=output.read(4*1024*1024+1)
    need(len(raw)<=4*1024*1024,'Git metadata byte bound');return raw

def authenticate_sources(root=ROOT):
    profile=authenticate_bytes(root)
    expected=dict(BASE_GIT_ROWS)
    for name,row in EXPECTED_FILES.items():expected[name]='100644 blob '+row['gitBlob']
    for name in (MODULE,PROFILE):expected[name]='100644 blob '+identity(read(root,name))['gitBlob']
    actual={}
    for item in git(root,'ls-tree','-r','-z','HEAD').split(b'\0'):
        if item:
            metadata,name=item.split(b'\t',1);name=name.decode();need(name not in actual,'duplicate tracked source');actual[name]=metadata.decode()
    need(same(actual,expected),'actual current HEAD source roster/bindings differ')
    need(not git(root,'status','--porcelain','--untracked-files=no').strip(),'actual current working files differ from HEAD')
    outputs=r'(?:member-company-(?:retained|test-results|control-results|observation-controls)|billing-(?:watchdog-results|lifetime-test-results))/[^\\\x00-\x1f]+\.(?:json|png|trx|log)'
    for name in git(root,'ls-files','--others','--exclude-standard','-z').split(b'\0'):
        if name:
            name=name.decode()
            need(re.fullmatch(outputs,name) is not None or name in ('member-company-build-web-tests.log','member-company-build-company-tests.log'),'unlisted current working input refused: '+name)
    return profile

def checked_dependency_sources(root=ROOT):
    for name,revision in [('Legacy.Maliev.ServiceDefaults',DEFAULTS),('Legacy.Maliev.CompatibilityContracts',CONTRACTS)]:
        path=Path(root)/'.dependencies'/name
        need(git(path,'rev-parse','HEAD').decode().strip()==revision,'actual dependency HEAD')
        need(not git(path,'status','--porcelain','--untracked-files=all').strip(),'actual dependency working source differs from HEAD')

def current_helper_source(path):
    path=Path(path);raw=read(path.parent,path.name)
    need(hashlib.sha256(raw).hexdigest()==HELPER,'exact accepted current lifetime helper required')
    return HELPER

def current_cleanup(value,legacy):
    need(type(value) is dict and type(value.get('children')) is list and len(value['children'])==3,'exact three current children')
    projected=copy.deepcopy(value)
    keys={'dispatched','identityCaptured','exitVerified','noChildVerified','readersClosed','released','process','cleanupStage'}
    for child in projected['children']:
        need(type(child) is dict and set(child)==keys and type(child['cleanupStage']) is str and child['cleanupStage']=='released','current child release stage required')
        child.pop('cleanupStage')
    result=legacy.cleanup(projected)
    for child in result['children']:child['cleanupStage']='released'
    return result

def validate_build_evidence(value,candidate):
    keys={'schemaVersion','candidateHead','profileSha256','sourceChecked','sourceRevisions','assemblies','packageSources','assets','buildLogs','generatedDefaultsMetadata','packageVerifications'}
    need(type(value) is dict and set(value)==keys and type(value['schemaVersion']) is int and value['schemaVersion']==2,'exact build evidence schema')
    need(re.fullmatch('[0-9a-f]{40}',candidate) and value['candidateHead']==candidate and value['sourceChecked'] is True,'actual executed candidate/source evidence')
    need(type(value['profileSha256']) is str and re.fullmatch('[0-9a-f]{64}',value['profileSha256']),'source profile hash')
    need(same(value['sourceRevisions'],dict(defaults=DEFAULTS,contracts=CONTRACTS)),'exact checked-out dependency revisions')
    need(type(value['assemblies']) is list and len(value['assemblies'])==4,'four actual assembly records')
    for row,(role,name,revision) in zip(value['assemblies'],ASSEMBLIES):
        need(type(row) is dict and set(row)=={'role','assemblyName','revision','version','mvid','sha256'} and (row['role'],row['assemblyName'],row['revision'])==(role,name,revision),'reviewed loaded assembly source')
        need(type(row['version']) is str and re.fullmatch(r'[0-9]+(?:\.[0-9]+){3}',row['version']),'actual assembly version')
        need(type(row['mvid']) is str and str(uuid.UUID(row['mvid']))==row['mvid'] and uuid.UUID(row['mvid']).int!=0,'actual assembly MVID')
        need(type(row['sha256']) is str and re.fullmatch('[0-9a-f]{64}',row['sha256']),'actual assembly hash')
    need(type(value['packageSources']) is list and len(value['packageSources'])==3,'exact selected package records')
    for row,(name,version,revision) in zip(value['packageSources'],PACKAGES):
        need(type(row) is dict and set(row)=={'id','version','revision','sha512'} and (row['id'],row['version'],row['revision'])==(name,version,revision),'unreviewed resolved package revision; retain evidence and review source')
        need(type(row['sha512']) is str and re.fullmatch('[A-Za-z0-9+/]{86}==',row['sha512']),'actual selected package SHA512')
    need(type(value['packageVerifications']) is list and len(value['packageVerifications'])==6,'six actual package verifier receipts')
    seen=set()
    for row,(package,project) in zip(value['packageVerifications'],[(p,q) for p in value['packageSources'] for q in ('companyTests','webTests')]):
        fields={'id','project','assetsContentHash','archiveHash','cacheArchiveHash','metadataContentHash','signaturePresent','verification'}
        need(type(row) is dict and set(row)==fields and row['id']==package['id'] and row['project']==project,'exact package/project verifier binding')
        need(row['signaturePresent'] is True and row['assetsContentHash']==row['metadataContentHash']==package['sha512'],'actual signed content hashes join')
        need(type(row['archiveHash']) is str and re.fullmatch('[A-Za-z0-9+/]{86}==',row['archiveHash']) and row['archiveHash']==row['cacheArchiveHash'],'actual archive hashes join')
        receipt=row['verification'];rkeys={'identity','pid','startTicks','bootId','executable','timeoutSeconds','outputLimitBytes','terminalVerified','pipeEof','pidfdAcquired','verified','exitCode','outputSha256','outputBytes','contentHash','elapsedMilliseconds'}
        need(type(receipt) is dict and set(receipt)==rkeys and receipt['identity']==package['id']+'.'+package['version'],'exact owned verifier receipt')
        need(all(receipt[k] is True for k in ('terminalVerified','pipeEof','pidfdAcquired','verified')) and type(receipt['exitCode']) is int and receipt['exitCode']==0 and receipt['contentHash']==package['sha512'],'positive settled signature verification')
        need(type(receipt['pid']) is int and receipt['pid']>0 and type(receipt['startTicks']) is str and receipt['startTicks'].isdigit() and int(receipt['startTicks'])>0 and type(receipt['bootId']) is str and re.fullmatch('[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}',receipt['bootId']) and type(receipt['executable']) is str and receipt['executable'].startswith('/') and receipt['executable'].endswith('/dotnet'),'actual verifier process identity')
        need(type(receipt['timeoutSeconds']) is int and receipt['timeoutSeconds']==30 and type(receipt['outputLimitBytes']) is int and receipt['outputLimitBytes']==65536 and type(receipt['outputBytes']) is int and 0<receipt['outputBytes']<=65536 and type(receipt['elapsedMilliseconds']) is int and 0<=receipt['elapsedMilliseconds']<=35000 and type(receipt['outputSha256']) is str and re.fullmatch('[0-9a-f]{64}',receipt['outputSha256']),'bounded verifier output and lifetime')
        marker=(receipt['bootId'],receipt['pid'],receipt['startTicks']);need(marker not in seen,'unique verifier process identity');seen.add(marker)
    for key,fields in [('assets',{'project','sha256'}),('buildLogs',{'project','sha256','warnings','errors'})]:
        need(type(value[key]) is list and len(value[key])==2,'two build project records')
        for row,name in zip(value[key],['web-tests','company-tests']):
            need(type(row) is dict and set(row)==fields and row['project']==name and re.fullmatch('[0-9a-f]{64}',row['sha256']),'exact actual build project/hash')
            if key=='buildLogs':need(type(row['warnings']) is int and row['warnings']==0 and type(row['errors']) is int and row['errors']==0,'actual strict build zero warnings/errors')
    metadata=value['generatedDefaultsMetadata']
    need(type(metadata) is dict and set(metadata)=={'sha256','revision'} and metadata['revision']==DEFAULTS and re.fullmatch('[0-9a-f]{64}',metadata['sha256']),'actual generated Defaults assembly metadata')
    return value

def load_build_evidence(root=ROOT):
    raw=read(root,BUILD);value=parse(raw);candidate=git(root,'rev-parse','HEAD').decode().strip()
    validate_build_evidence(value,candidate)
    need(value['profileSha256']==identity(read(root,PROFILE))['sha256'],'build evidence/source profile join')
    from capture_build_source import inspect_assembly
    base='acceptance/MemberCompanyCatalogPersistence/Tests/bin/Release/net10.0/'
    for row in value['assemblies']:
        actual=inspect_assembly(read(root,base+row['assemblyName']+'.dll',16*1024*1024))
        need(all(actual[k]==row[k] for k in ('sha256','mvid','version')),'actual built DLL differs from build evidence')
    return value,identity(raw)['sha256']

def current_receipt(value,culture,candidate,evidence,evidence_sha,legacy):
    validate_build_evidence(evidence,candidate)
    need(type(value) is dict and type(value.get('runtimeRetryPolicy')) is dict,'current runtime retry policy')
    policy=value['runtimeRetryPolicy']
    original={'clientName','runtimeDefaultsRevision','handlerCount','pipelineOptions','pipelineOrderFromPinnedSource','runtimePackageSourceRevision','confirmedFromActualHandlerChain'}
    extra={'assemblyEvidence','linkedHandlerOrder','linkedHandlerOrderVerified','buildEvidenceSha256','sourceProfileSha256'}
    need(set(policy)==original|extra,'exact current runtime evidence schema')
    need(policy['runtimeDefaultsRevision']==DEFAULTS and policy['runtimePackageSourceRevision']==RESILIENCE,'actual current loaded source revisions')
    need(same(policy['assemblyEvidence'],evidence['assemblies']),'observed loaded DLL/MVID/source must join actual build')
    need(same(policy['linkedHandlerOrder'],[0,1]) and policy['linkedHandlerOrderVerified'] is True,'actual same-instance linked handler order required')
    need(policy['buildEvidenceSha256']==evidence_sha and policy['sourceProfileSha256']==evidence['profileSha256'],'actual build/source-profile receipt joins')
    projected=copy.deepcopy(value);old=projected['runtimeRetryPolicy']
    for key in extra:old.pop(key)
    old['runtimeDefaultsRevision']='3c790ba6414b2a539f24aabb6948549ffd81a86b'
    old['runtimePackageSourceRevision']='02107c65bab30aad9e35b5133ed643eaa77bccd8'
    legacy.receipt(projected,culture,candidate)
    return copy.deepcopy(value)

def observation_controls(path):
    path=Path(path);raw=read(path.parent,path.name,4*1024*1024);doc=ET.fromstring(raw);ns='{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'
    need(doc.tag==ns+'TestRun','actual observation-control TRX namespace')
    results=doc.findall(ns+'Results/'+ns+'UnitTestResult');definitions=doc.findall(ns+'TestDefinitions/'+ns+'UnitTest');entries=doc.findall(ns+'TestEntries/'+ns+'TestEntry')
    need(len(results)==len(definitions)==len(entries)==6,'exact six complete observation-control joins')
    counters=doc.findall(ns+'ResultSummary/'+ns+'Counters');need(len(counters)==1,'one observation-control summary')
    expected=dict(total='6',executed='6',passed='6',**{key:'0' for key in NONPASSING});need(all(counters[0].get(k)==v for k,v in expected.items()),'all six controls passed; no incomplete/skipped rows')
    def guid(value):
        need(type(value) is str and str(uuid.UUID(value))==value and uuid.UUID(value).int!=0,'canonical nonempty native GUID');return value
    by_id={guid(row.get('id')):row for row in definitions};need(len(by_id)==6,'unique observation definitions')
    names=[];pairs=[]
    for row in results:
        test=guid(row.get('testId'));execution=guid(row.get('executionId'));need(test in by_id,'joined observation definition')
        definition=by_id[test];runs=definition.findall(ns+'Execution');need(len(runs)==1 and guid(runs[0].get('id'))==execution,'original definition execution join')
        need(definition.get('name')==row.get('testName') and row.get('outcome')=='Passed','exact native observation name/outcome');names.append(row.get('testName'));pairs.append((test,execution))
    need(len(set(names))==6 and set(names)==set(OBSERVATION_NAMES) and len(set(pairs))==6 and len({x[0] for x in pairs})==6 and len({x[1] for x in pairs})==6,'exact six original observation identities/executions')
    joined=[(guid(row.get('testId')),guid(row.get('executionId'))) for row in entries];need(len(set(joined))==6 and set(joined)==set(pairs),'complete native observation entry joins')
    need(not doc.findall('.//'+ns+'ErrorInfo'),'no observation error records')
    return dict(passed=6,failed=0,skipped=0,rawTrxSha256=identity(raw)['sha256'])

def verify_observation_controls(root=ROOT):
    folder=Path(root)/'member-company-observation-controls';files=list(folder.glob('*.trx'));need(len(files)==1,'one actual observation-control TRX')
    return observation_controls(files[0])

def source_only_main():
    authenticate_sources()
    if os.environ.get('GITHUB_ACTIONS')=='true':checked_dependency_sources()
    print('Exact current Company source/HEAD profile verified; no native grant or runtime acceptance')

if __name__=='__main__':
    import sys
    if sys.argv[1:]==['--verify-observation-controls']:print(json.dumps(verify_observation_controls()))
    else:
        need(not sys.argv[1:],'fixed source-only command; no caller-selected root/profile')
        source_only_main()
