// Public mathematical primitives only. Compile against the pinned OCCT object cache;
// write STEP, then import with the unchanged promoted production WASM pair.
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakePrism.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Common.hxx>
#include <BRepPrimAPI_MakeHalfSpace.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <STEPControl_Writer.hxx>
#include <StepData_StepWriter.hxx>
#include <StepData_Protocol.hxx>
#include <XSControl_WorkSession.hxx>
#include <ShapeUpgrade_ShapeDivideClosedEdges.hxx>
#include <fstream>
#include <gp_Elips.hxx>
#include <gp_Pln.hxx>
#include <emscripten.h>
#include <stdexcept>
void save(const TopoDS_Shape& shape, const char* name) {
  STEPControl_Writer writer;
  if (writer.Transfer(shape, STEPControl_AsIs) != IFSelect_RetDone)
    throw std::runtime_error("STEP fixture export failed");
  // Preserve binary64 round trips, including the periodic seam's 2*pi ordinate.
  // Default STEP writer's 12 digits legitimately fails the strict band policy.
  StepData_StepWriter precise(writer.Model());
  precise.FloatWriter().SetFormat("%.17E");
  precise.SendModel(Handle(StepData_Protocol)::DownCast(writer.WS()->Protocol()));
  std::ofstream output(name); if (!precise.Print(output)) throw std::runtime_error("STEP fixture write failed");
}
int main() {
  auto ring = BRepAlgoAPI_Cut(BRepPrimAPI_MakeCylinder(22.25,14.8).Shape(), BRepPrimAPI_MakeCylinder(10.7,14.8).Shape()).Shape();
  STEPControl_Writer ordinary;
  if (ordinary.Transfer(ring,STEPControl_AsIs) != IFSelect_RetDone || ordinary.Write("/ring-default-precision.step") != IFSelect_RetDone)
    throw std::runtime_error("default precision fixture export failed");
  save(ring,"/ring.step");
  save(BRepAlgoAPI_Cut(BRepPrimAPI_MakeCylinder(22.25,14.8).Shape(),
    BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(0,0,14.8),-gp::DZ()),10.7,14.8).Shape()).Shape(),"/ring-flipped-axis.step");
  ShapeUpgrade_ShapeDivideClosedEdges split(ring); split.SetNbSplitPoints(1); split.Perform();
  save(split.Result(),"/ring-split-edges.step");
  gp_Trsf transform; transform.SetRotation(gp_Ax1(gp::Origin(),gp_Dir(1,2,3)),0.7);
  transform.SetTranslationPart(gp_Vec(21,-32,43));
  save(BRepBuilderAPI_Transform(ring,transform,true).Shape(),"/ring-transformed.step");
  auto wire=BRepBuilderAPI_MakeWire(BRepBuilderAPI_MakeEdge(gp_Elips(gp_Ax2(gp::Origin(),gp::DZ()),10,5))).Wire();
  auto face=BRepBuilderAPI_MakeFace(gp_Pln(gp::XOY()),wire).Face();
  save(BRepPrimAPI_MakePrism(face,gp_Vec(0,0,4)).Shape(),"/ellipse-prism.step");
  auto cap=BRepBuilderAPI_MakeFace(gp_Pln(gp_Pnt(0,0,10),gp_Dir(0,0.6,0.8))).Face();
  auto half=BRepPrimAPI_MakeHalfSpace(cap,gp_Pnt(0,0,0)).Solid();
  save(BRepAlgoAPI_Common(BRepPrimAPI_MakeCylinder(5,20).Shape(),half).Shape(),"/ellipse-cap.step");
  save(BRepPrimAPI_MakeCylinder(8,6).Shape(),"/disk-cylinder.step");
  save(BRepPrimAPI_MakeCylinder(8,6,1.5).Shape(),"/partial-cylinder.step");
  auto blank=BRepPrimAPI_MakeCylinder(20,20).Shape();
  auto bottom=BRepPrimAPI_MakeCylinder(5,7).Shape();
  auto top=BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(0,0,13),gp::DZ()),5,7).Shape();
  save(BRepAlgoAPI_Cut(BRepAlgoAPI_Cut(blank,bottom).Shape(),top).Shape(),"/opposite-blind.step");
  auto bore=BRepPrimAPI_MakeCylinder(5,20).Shape();
  auto counter=BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(0,0,12),gp::DZ()),8,8).Shape();
  save(BRepAlgoAPI_Cut(BRepAlgoAPI_Cut(blank,bore).Shape(),counter).Shape(),"/stepped-bore.step");
  EM_ASM({ const fs=require('fs'); for(const n of ['ring','ring-flipped-axis','ring-default-precision','ring-split-edges','ring-transformed','ellipse-prism','ellipse-cap','disk-cylinder','partial-cylinder','opposite-blind','stepped-bore']) fs.writeFileSync('/tmp/region-fixture-'+n+'.step',FS.readFile('/'+n+'.step')); });
}
