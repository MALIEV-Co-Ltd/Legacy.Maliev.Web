#include "kernel-face.hpp"
#include "kernel-body.hpp"
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_Copy.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRep_Builder.hxx>
#include <gp_Circ.hxx>
#include <gp_Pln.hxx>
#include <iostream>
#include <stdexcept>
#include <limits>
using namespace MalievKernel;
class RegionTestMesh:public Mesh,public KernelMeshSource {
 public:
  TopoDS_Shape shape;bool standalone=false;
  explicit RegionTestMesh(const TopoDS_Shape& value):shape(value){}
  const TopoDS_Shape& KernelShape()const override{return shape;}
  bool IsStandaloneFaceGroup()const override{return standalone;}
  std::string GetName()const override{return "fixture";}
  bool GetColor(Color&)const override{return false;}
  void EnumerateFaces(const std::function<void(const Face&)>&)const override{}
};
int main() {
  int checks=0;
  auto check=[&](bool ok,const char* name){++checks;if(!ok)throw std::runtime_error(name);};
  auto near=[&](double x,double expected,const char* name){check(std::abs(x-expected)<1e-7,name);};
  try {
    const double pi=std::acos(-1.0);
    auto circle=[](double r,double x){return BRepBuilderAPI_MakeWire(BRepBuilderAPI_MakeEdge(gp_Circ(gp_Ax2(gp_Pnt(x,0,0),gp::DZ()),r))).Wire();};
    auto disk=BRepBuilderAPI_MakeFace(gp_Pln(gp::XOY()),circle(5,0)).Face();
    auto a=RegionMeasures::Area(disk,true);
    check(a.available,"disk area available"); near(a.value,25*pi,"disk trimmed area");
    BRepBuilderAPI_MakeFace annulus(gp_Pln(gp::XOY()),circle(5,0));
    annulus.Add(TopoDS::Wire(circle(1,0).Reversed()));
    auto ring=annulus.Face(); a=RegionMeasures::Area(ring,true);
    check(a.available,"annulus available"); near(a.value,24*pi,"inner loop excluded"); near(a.centroid.X(),0,"annulus centroid may lie in hole");
    BRepBuilderAPI_MakeFace offset(gp_Pln(gp::XOY()),circle(5,0));
    offset.Add(TopoDS::Wire(circle(1,2).Reversed())); a=RegionMeasures::Area(offset.Face(),true);
    check(a.available,"off-center hole available"); near(a.value,24*pi,"off-center area"); near(a.centroid.X(),-1.0/12,"off-center centroid");
    gp_Trsf transform; transform.SetRotation(gp_Ax1(gp::Origin(),gp::DY()),pi/2); transform.SetTranslationPart(gp_Vec(10,20,30));
    a=RegionMeasures::Area(TopoDS::Face(offset.Face().Moved(TopLoc_Location(transform))),true);
    check(a.available,"located area available"); near(a.centroid.X(),10,"located x once"); near(a.centroid.Y(),20,"located y once"); near(a.centroid.Z(),30+1.0/12,"rotated centroid once");
    auto box=BRepPrimAPI_MakeBox(2,4,6).Solid();
    auto volume=RegionMeasures::Volume(box,true,true);
    check(volume.available,"box volume available");near(volume.value,48,"box volume");near(volume.centroid.X(),1,"box centroid");
    volume=RegionMeasures::Volume(TopoDS::Solid(box.Moved(TopLoc_Location(transform))),true,true);
    check(volume.available,"located solid available");near(volume.value,48,"rigid volume invariant");near(volume.centroid.X(),13,"rotated volume centroid x");near(volume.centroid.Z(),29,"rotated volume centroid z");
    volume=RegionMeasures::Volume(BRepPrimAPI_MakeCylinder(2,7).Solid(),true,true);
    check(volume.available,"cylinder available");near(volume.value,28*pi,"cylinder volume");near(volume.centroid.Z(),3.5,"cylinder centroid");
    BRepMesh_IncrementalMesh mesh(ring,.5); const auto coarse=RegionMeasures::Area(ring,true);
    BRepMesh_IncrementalMesh fine(ring,.01); a=RegionMeasures::Area(ring,true);
    check(a.available&&coarse.available,"triangulated native face available");near(a.value,coarse.value,"tessellation does not determine area");
    TopLoc_Location location; auto triangles=BRep_Tool::Triangulation(ring,location);
    check(!triangles.IsNull(),"negative fixture has triangulation");
    TopoDS_Face meshOnly; BRep_Builder builder;builder.MakeFace(meshOnly,triangles);
    check(!RegionMeasures::Area(meshOnly,true).available,"mesh-only support rejected");
    const auto copied=BRepBuilderAPI_Copy(box,Standard_True,Standard_False).Shape();
    BRepMesh_IncrementalMesh copiedMesh(copied,.1);
    TopExp_Explorer copiedFaces(copied,TopAbs_FACE);auto unsupportedFace=TopoDS::Face(copiedFaces.Current());
    check(!BRep_Tool::Triangulation(unsupportedFace,location).IsNull(),"volume negative fixture includes mesh");
    builder.UpdateFace(unsupportedFace,Handle(Geom_Surface)(),TopLoc_Location(),.0000001);
    check(RegionMeasures::Volume(copied,true,true).reason=="native_surface_unavailable","one missing solid face support rejects all mesh-backed volume");
    check(!RegionMeasures::Area(disk,false).available,"unverified units rejected");
    check(!RegionMeasures::Area(TopoDS_Face(),true).available,"null face rejected");
    check(!RegionMeasures::Volume(box,true,false).available,"invalid membership rejected");
    auto noAttempt=RegionMeasures::Export(RegionMeasures::Volume(box,true,false),"body-0");
    check(noAttempt["requestedRelativeError"].isNull()&&noAttempt["integrationAttempts"]["length"].as<int>()==0,"native precondition failure reports no attempted request");
    check(!RegionMeasures::Volume(TopoDS::Solid(box.Reversed()),true,true).available,"inverted solid rejected without absolute value");
    TopoDS_Solid empty;builder.MakeSolid(empty);
    check(!RegionMeasures::Volume(empty,true,true).available,"empty volume not zero success");
    TopoDS_Solid cavity;builder.MakeSolid(cavity);
    for(TopoDS_Iterator it(box);it.More();it.Next())builder.Add(cavity,it.Value());
    auto inner=BRepPrimAPI_MakeBox(gp_Pnt(.5,.5,.5),1,1,1).Solid();
    for(TopoDS_Iterator it(inner);it.More();it.Next())builder.Add(cavity,it.Value().Reversed());
    check(RegionMeasures::Volume(cavity,true,true).reason=="single_shell_solid_required","cavity unsupported without subset integration");
    TopoDS_Shell open;builder.MakeShell(open);int faceCount=0;
    for(TopExp_Explorer ex(box,TopAbs_FACE);ex.More();ex.Next())if(++faceCount>1)builder.Add(open,ex.Current());
    TopoDS_Solid openSolid;builder.MakeSolid(openSolid);builder.Add(openSolid,open);
    check(!RegionMeasures::Volume(openSolid,true,true).available,"open solid rejected before OnlyClosed can skip shell");
    check(!RegionMeasures::Volume(open,true,true).available,"standalone shell rejected");
    BRepBuilderAPI_MakeFace wrongHole(gp_Pln(gp::XOY()),circle(5,0));wrongHole.Add(circle(1,0));
    check(!RegionMeasures::Area(wrongHole.Face(),true).available,"wrong wire orientation rejected");
    gp_Trsf far;far.SetTranslation(gp_Vec(1000000,-2000000,3000000));
    a=RegionMeasures::Area(TopoDS::Face(disk.Moved(TopLoc_Location(far))),true);
    check(a.available,"far-origin area remains native numerical estimate");near(a.centroid.X(),1000000,"far-origin centroid");
    check(!RegionMeasures::Accept(0,gp::Origin(),0).available,"zero mass rejected");
    check(!RegionMeasures::Accept(-1,gp::Origin(),0).available,"negative mass rejected");
    check(!RegionMeasures::Accept(1,gp::Origin(),.001).available,"target not met rejected");
    check(!RegionMeasures::Accept(1,gp::Origin(),-1).available,"negative estimate rejected");
    check(!RegionMeasures::Accept(std::numeric_limits<double>::infinity(),gp::Origin(),0).available,"nonfinite mass rejected");
    check(!RegionMeasures::Accept(1,gp_Pnt(NAN,0,0),0).available,"nonfinite centroid rejected");
    check(!RegionMeasures::Accept(1,gp::Origin(),NAN).available,"nonfinite error rejected");
    for(int acceptedAttempt=1;acceptedAttempt<=3;++acceptedAttempt){
      int calls=0;
      auto retried=RegionMeasures::VolumeAttempts([&](double eps){
        ++calls;near(eps,calls==1?.0001:calls==2?.00001:.000001,"bounded stricter request sequence");
        return RegionMeasures::Accept(48,gp_Pnt(1,2,3),calls==acceptedAttempt?.00005:.002);
      });
      check(retried.available&&calls==acceptedAttempt,"stop first successful fixed-target attempt");
      check(static_cast<int>(retried.attempts.size())==acceptedAttempt,"complete actual attempts only");
      val record=RegionMeasures::Export(retried,"body-0");
      near(record["acceptanceRelativeError"].as<double>(),.0001,"acceptance target never relaxed");
      near(record["requestedRelativeError"].as<double>(),acceptedAttempt==1?.0001:acceptedAttempt==2?.00001:.000001,"accepted request exported separately");
    }
    int calls=0;
    auto exhausted=RegionMeasures::VolumeAttempts([&](double){++calls;return RegionMeasures::Accept(48,gp::Origin(),.002);});
    check(!exhausted.available&&calls==3,"target exhaustion stops after three attempts");
    val exhaustedRow=RegionMeasures::Export(exhausted,"body-0");
    check(exhaustedRow["value"].isNull()&&exhaustedRow["centroidMm"].isNull()&&exhaustedRow["estimatedRelativeVolumeError"].isNull(),"exhausted final numeric values remain null");
    near(exhaustedRow["integrationAttempts"][2]["estimatedRelativeVolumeError"].as<double>(),.002,"failed finite estimate retained as diagnostic provenance");
    for(const auto invalid:{RegionMeasures::Accept(0,gp::Origin(),0),RegionMeasures::Accept(1,gp::Origin(),NAN),RegionMeasures::Unavailable("native_surface_unavailable")}){
      calls=0;auto stopped=RegionMeasures::VolumeAttempts([&](double){++calls;return invalid;});
      check(!stopped.available&&calls==1,"invalid result never retries");
    }
    calls=0;auto thrown=RegionMeasures::VolumeAttempts([&](double)->RegionMeasures::Result{++calls;throw Standard_Failure("fixture integration exception");});
    check(!thrown.available&&calls==1&&thrown.reason=="native_integration_failed","native exception never retries");
    RegionTestMesh nativeMesh(box);ExportContext context(0);int index=0;
    for(TopExp_Explorer ex(box,TopAbs_FACE);ex.More();ex.Next()){
      const auto face=TopoDS::Face(ex.Current());OcctFace source(face);val row=val::object();
      WriteFace(source,row,0,index++,context,true);
      check(row["nativeRegionMeasures"]["status"].as<std::string>()=="available","actual face export available");
    }
    val exported=val::object();WriteBody(nativeMesh,exported,context,true);
    check(exported["kernelBody"]["nativeRegionMeasures"]["status"].as<std::string>()=="available","actual complete body export available");
    context.sourceFaces.push_back(context.sourceFaces[0]);context.sourceFaceRecords.push_back(context.sourceFaceRecords[0]);
    WriteBody(nativeMesh,exported,context,true);
    check(exported["kernelBody"]["nativeRegionMeasures"]["reason"].as<std::string>()=="incomplete_face_membership","actual duplicate membership rejected");
    check(exported["kernelBody"]["nativeRegionMeasures"]["value"].isNull(),"unavailable export does not leak former volume");
    context.sourceFaces.pop_back();context.sourceFaceRecords.pop_back();context.sourceFaces.pop_back();context.sourceFaceRecords.pop_back();
    WriteBody(nativeMesh,exported,context,true);
    check(exported["kernelBody"]["nativeRegionMeasures"]["status"].as<std::string>()=="unavailable","actual missing face membership rejected");
    nativeMesh.standalone=true;WriteBody(nativeMesh,exported,context,true);
    check(exported["kernelBody"]["nativeRegionMeasures"]["status"].as<std::string>()=="unavailable","standalone group cannot integrate enclosing solid");
    std::cout<<"PASS: "<<checks<<" native region measure checks\n";
  } catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
