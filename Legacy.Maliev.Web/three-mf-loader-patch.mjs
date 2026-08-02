function replaceRequired(source, search, replacement) {
  if (!source.includes(search)) {
    throw new Error('The pinned ThreeMF loader shape changed; refusing to build an unverified patch.');
  }

  return source.replace(search, replacement);
}

export function patchThreeMfLoader(source) {
  let patched = source;
  patched = replaceRequired(
    patched,
    'function getBuild( data, objects, modelData, textureData, objectData, builder )',
    'function getBuild( data, objects, modelData, textureData, objectData, builder, modelsData )');
  patched = replaceRequired(
    patched,
    'builder( data, objects, modelData, textureData, objectData )',
    'builder( data, objects, modelData, textureData, objectData, modelsData )');
  patched = replaceRequired(
    patched,
    'function buildComposite( compositeData, objects, modelData, textureData )',
    'function buildComposite( compositeData, objects, modelData, textureData, objectData, modelsData )');
  patched = replaceRequired(
    patched,
    'buildObject( component.objectId, objects, modelData, textureData );',
    'buildObject( component.objectId, objects, modelData, textureData, modelsData );');
  patched = replaceRequired(
    patched,
    'build = objects[ component.objectId ];\n\n\t\t\t\t}\n\n\t\t\t\tconst object3D = build.clone();',
    `build = objects[ component.objectId ];

\t\t\t\t}

\t\t\t\tif ( build === undefined ) {

\t\t\t\t\tthrow new Error( '3MF component references an unknown object.' );

\t\t\t\t}

\t\t\t\tconst object3D = build.clone();`);
  patched = replaceRequired(
    patched,
    'function buildObject( objectId, objects, modelData, textureData )',
    'function buildObject( objectId, objects, modelData, textureData, modelsData )');
  patched = replaceRequired(
    patched,
    "const objectData = modelData[ 'resources' ][ 'object' ][ objectId ];",
    `let objectData = modelData && modelData[ 'resources' ] && modelData[ 'resources' ][ 'object' ]
\t\t\t\t? modelData[ 'resources' ][ 'object' ][ objectId ]
\t\t\t\t: undefined;

\t\t\t\tif ( objectData === undefined && modelsData ) {

\t\t\t\t\tfor ( const candidateModelData of Object.values( modelsData ) ) {

\t\t\t\t\t\tconst candidateObjects = candidateModelData && candidateModelData[ 'resources' ]
\t\t\t\t\t\t\t? candidateModelData[ 'resources' ][ 'object' ]
\t\t\t\t\t\t\t: undefined;
\t\t\t\t\t\tif ( candidateObjects && candidateObjects[ objectId ] !== undefined ) {

\t\t\t\t\t\t\tmodelData = candidateModelData;
\t\t\t\t\t\t\tobjectData = candidateObjects[ objectId ];
\t\t\t\t\t\t\tbreak;

\t\t\t\t\t\t}

\t\t\t\t\t}

\t\t\t\t}

\t\t\t\tif ( objectData === undefined ) {

\t\t\t\t\tthrow new Error( '3MF object reference could not be resolved.' );

\t\t\t\t}`);
  patched = replaceRequired(
    patched,
    'getBuild( meshData, objects, modelData, textureData, objectData, buildGroup )',
    'getBuild( meshData, objects, modelData, textureData, objectData, buildGroup, modelsData )');
  patched = replaceRequired(
    patched,
    'getBuild( compositeData, objects, modelData, textureData, objectData, buildComposite )',
    'getBuild( compositeData, objects, modelData, textureData, objectData, buildComposite, modelsData )');
  patched = replaceRequired(
    patched,
    'buildObject( objectId, objects, modelData, textureData );',
    'buildObject( objectId, objects, modelData, textureData, modelsData );');
  return patched;
}
