﻿/*
DOCUMENTATION- phil, 12/16/19

This class manages all the interaction in the scene.
It relies on ThermoState to keep track of any thermodynamic-centric state, but other than that, this is responsible for everything moving about the scene.
It should be instantiated as a game object "Oracle" at the root of the scene heirarchy.
There are unfortunately somewhat inconsistent patterns of what variables are defined publicly via the editor inspector, and which are set in code, though I tried to err toward the latter where possible.
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class World : MonoBehaviour
{
  const float CONTAINER_INSULATION_COEFFICIENT = 0.1f; // Not really based on a physical material, just a way to roughly simulate imperfect insulation.
  public Material hand_empty;
  Material[] hand_emptys;
  public Material hand_touching;
  Material[] hand_touchings;
  public Material hand_grabbing;
  Material[] hand_grabbings;
  public Material tab_default;
  public Material tab_hi;
  public Material tab_sel;
  public Material tab_hisel;
  public DirectionalIndicator arrows;

  ThermoState thermo;
  GameObject cam_offset;
  GameObject ceye;
  GameObject lhand;
  GameObject lactualhand;
  Vector3 lhand_pos;
  Vector3 lhand_vel;
  SkinnedMeshRenderer lhand_meshrenderer;
  GameObject rhand;
  GameObject ractualhand;
  Vector3 rhand_pos;
  Vector3 rhand_vel;
  SkinnedMeshRenderer rhand_meshrenderer;

  GameObject vrcenter;
  FingerToggleable vrcenter_fingertoggleable;
  MeshRenderer vrcenter_backing_meshrenderer;

  List<Touchable> movables;
  GameObject workspace;
  GameObject handle_workspace;
  Touchable handle_workspace_touchable;
  GameObject lgrabbed = null;
  GameObject rgrabbed = null;
  int lhtrigger_delta = 0;
  int litrigger_delta = 0;
  int rhtrigger_delta = 0;
  int ritrigger_delta = 0;
  float lz = 0f;
  float rz = 0f;
  float ly = 0f;
  float ry = 0f;

  List<Tool> tools;
  Tool tool_insulator;
  Tool tool_clamp;
  Tool tool_burner;
  Tool tool_coil;
  Tool tool_weight;
  Tool tool_balloon;

  ParticleSystem flame; //special case

  bool halfed = false;
  List<Halfable> halfables;
  GameObject halfer;
  Touchable halfer_touchable;
  GameObject reset;
  Touchable reset_touchable;

  GameObject vessel;
  GameObject graph;
  GameObject state_dot;
  GameObject challenge_dot;
  GameObject clipboard;

  ChallengeBall challenge_ball_collide;

  bool lhtrigger = false;
  bool rhtrigger = false;
  bool litrigger = false;
  bool ritrigger = false;

  GameObject instructions_parent;
  GameObject challenge_parent;
  GameObject quiz_parent;
  List<Tab> mode_tabs;
  int board_mode = 0; //0- instructions, 1- challenge, 2- quiz, 3- congrats
  int question = 0;
  List<string> questions;
  List<string> options;
  List<int> answers;
  List<int> givens;
  List<Tab> option_tabs;
  Tab qconfirm_tab;
  bool qconfirmed = false;
  GameObject board;
  TextMeshPro qtext_tmp;
  int qselected = -1;

  double applied_heat = 0;
  double applied_weight = 0;

  // Start is called before the first frame update
  void Start()
  {
    // All this code does, at end of day, is find all the objects to manage,
    // and set initial values and such as needed.
    thermo = GameObject.Find("Oracle").GetComponent<ThermoState>();

    cam_offset = GameObject.Find("CamOffset");
    ceye = GameObject.Find("CenterEyeAnchor");

    hand_emptys    = new Material[]{hand_empty};
    hand_touchings = new Material[]{hand_touching};
    hand_grabbings = new Material[]{hand_grabbing};

    lhand  = GameObject.Find("LeftControllerAnchor");
    lactualhand = lhand.transform.GetChild(0).gameObject;
    lhand_pos = lhand.transform.position;
    lhand_vel = new Vector3(0f,0f,0f);
    lhand_meshrenderer = GameObject.Find("hands:Lhand").GetComponent<SkinnedMeshRenderer>();

    rhand  = GameObject.Find("RightControllerAnchor");
    ractualhand = rhand.transform.GetChild(0).gameObject;
    rhand_pos = rhand.transform.position;
    rhand_vel = new Vector3(0f,0f,0f);
    rhand_meshrenderer = GameObject.Find("hands:Rhand").GetComponent<SkinnedMeshRenderer>();

    lhand_meshrenderer.materials = hand_emptys;
    rhand_meshrenderer.materials = hand_emptys;

    // As we grab them, set ranges on tool dials (sliders).
    Tool t;
    tools = new List<Tool>();
    t = GameObject.Find("Tool_Insulator").GetComponent<Tool>(); tool_insulator = t; tools.Add(t); t.dial_dial.min_map =  0f; t.dial_dial.max_map = 1f; t.dial_dial.unit = "n";
    t = GameObject.Find("Tool_Clamp").GetComponent<Tool>(); tool_clamp     = t; tools.Add(t); t.dial_dial.min_map =  0f; t.dial_dial.max_map = 1f; t.dial_dial.unit = "h";
    t = GameObject.Find("Tool_Burner"   ).GetComponent<Tool>(); tool_burner    = t; tools.Add(t); t.dial_dial.min_map =  1f; t.dial_dial.max_map =  1000f*100f; t.dial_dial.unit = "J/s";
    t = GameObject.Find("Tool_Coil"     ).GetComponent<Tool>(); tool_coil      = t; tools.Add(t); t.dial_dial.min_map = -1f; t.dial_dial.max_map = -1000f*100f; t.dial_dial.unit = "J/s";
    double kg_corresponding_to_10mpa = thermo.surfacearea_insqr*(10*1453.8/*MPa->psi*/)*0.453592/*lb->kg*/;
    double kg_corresponding_to_2mpa = thermo.surfacearea_insqr*(2*1453.8/*MPa->psi*/)*0.453592/*lb->kg*/; // 10 MPa seems way too big, sooooo... we'll just do 2 MPa.
    t = GameObject.Find("Tool_Weight"   ).GetComponent<Tool>(); tool_weight    = t; tools.Add(t); t.dial_dial.min_map =  0f; t.dial_dial.max_map =  (float)kg_corresponding_to_10mpa; t.dial_dial.unit = "kg";
    t = GameObject.Find("Tool_Balloon"  ).GetComponent<Tool>(); tool_balloon   = t; tools.Add(t); t.dial_dial.min_map =  0f; t.dial_dial.max_map = -(float)kg_corresponding_to_10mpa; t.dial_dial.unit = "kg";

    flame = GameObject.Find("Flame").GetComponent<ParticleSystem>();

    workspace = GameObject.Find("Workspace");
    handle_workspace = GameObject.Find("Handle_Workspace");
    handle_workspace_touchable = handle_workspace.GetComponent<Touchable>();

    // set initial states of meshrenderers and transforms for our tools.
    for(int i = 0; i < tools.Count; i++)
    {
      t = tools[i];
      t.active_available_meshrenderer.enabled = false;
      t.active_snap_meshrenderer.enabled = false;
      t.storage_available_meshrenderer.enabled = false;
      t.storage_snap_meshrenderer.enabled = false;
      GameObject g = t.gameObject;
      g.transform.SetParent(t.storage.gameObject.transform);
      t.stored = true;
      g.transform.localPosition = new Vector3(0f,0f,0f);
      g.transform.localScale = new Vector3(1f,1f,1f);
      g.transform.localRotation = Quaternion.identity;
      float v = t.storage.transform.localScale.x; //can grab any dimension
      Vector3 invscale = new Vector3(1f/v,1f/v,1f/v);
      t.text.transform.localScale = invscale;
      t.textv_tmpro.SetText("{0:3}"+t.dial_dial.unit,(float)t.dial_dial.map);
    }

    vessel = GameObject.Find("Vessel");
    graph = GameObject.Find("Graph");
    state_dot = GameObject.Find("gstate");
    challenge_dot = GameObject.Find("cstate");
    clipboard = GameObject.Find("Clipboard");

    challenge_ball_collide = challenge_dot.GetComponent<ChallengeBall>();

    vrcenter = GameObject.Find("VRCenter");
    vrcenter_fingertoggleable = vrcenter.GetComponent<FingerToggleable>();
    vrcenter_backing_meshrenderer = vrcenter.transform.GetChild(1).GetComponent<MeshRenderer>();

    movables = new List<Touchable>();
    for(int i = 0; i < tools.Count; i++) movables.Add(tools[i].touchable); //important that tools take priority, so they can be grabbed and removed
    movables.Add(graph.GetComponent<Touchable>());
    movables.Add(clipboard.GetComponent<Touchable>());

    halfer = GameObject.Find("Halfer");
    halfer_touchable = halfer.GetComponent<Touchable>();
    reset = GameObject.Find("Reset");
    reset_touchable = reset.GetComponent<Touchable>();
    halfables = new List<Halfable>();
    halfables.Add(GameObject.Find("Container"     ).GetComponent<Halfable>());
    halfables.Add(GameObject.Find("Tool_Insulator").GetComponent<Halfable>());
    halfables.Add(GameObject.Find("Tool_Coil"     ).GetComponent<Halfable>());

    // A bunch of stuff related to initializing clipboard.
    instructions_parent = GameObject.Find("Instructions");
    challenge_parent = GameObject.Find("Challenge");
    quiz_parent = GameObject.Find("Quiz");

    mode_tabs = new List<Tab>();
    mode_tabs.Add(GameObject.Find("ModeInstructions").GetComponent<Tab>());
    mode_tabs.Add(GameObject.Find("ModeChallenge").GetComponent<Tab>());
    mode_tabs.Add(GameObject.Find("ModeQuiz").GetComponent<Tab>());

    questions = new List<string>();
    options = new List<string>();
    answers = new List<int>(); //the correct answer
    givens = new List<int>(); //the recorded answer given by the user (default -1)

    questions.Add("Here is an example question- in what region is the water?");
    options.Add("A. Solid");
    options.Add("B. Liquid");
    options.Add("C. Vapor");
    options.Add("D. Two Phase");
    answers.Add(2);
    givens.Add(-1);

    questions.Add("Here is an example question- in what region is the water?");
    options.Add("A. Solid");
    options.Add("B. Liquid");
    options.Add("C. Vapor");
    options.Add("D. Two Phase");
    answers.Add(2);
    givens.Add(-1);

    questions.Add("Here is an example question- in what region is the water?");
    options.Add("A. Solid");
    options.Add("B. Liquid");
    options.Add("C. Vapor");
    options.Add("D. Two Phase");
    answers.Add(2);
    givens.Add(-1);

    questions.Add("Here is an example question- in what region is the water?");
    options.Add("A. Solid");
    options.Add("B. Liquid");
    options.Add("C. Vapor");
    options.Add("D. Two Phase");
    answers.Add(2);
    givens.Add(-1);

    questions.Add("Here is an example question- in what region is the water?");
    options.Add("A. Solid");
    options.Add("B. Liquid");
    options.Add("C. Vapor");
    options.Add("D. Two Phase");
    answers.Add(2);
    givens.Add(-1);

    option_tabs = new List<Tab>();
    option_tabs.Add(GameObject.Find("QA").GetComponent<Tab>());
    option_tabs.Add(GameObject.Find("QB").GetComponent<Tab>());
    option_tabs.Add(GameObject.Find("QC").GetComponent<Tab>());
    option_tabs.Add(GameObject.Find("QD").GetComponent<Tab>());
    qconfirm_tab = GameObject.Find("QConfirm").GetComponent<Tab>();
    board = GameObject.Find("Board");
    qtext_tmp = GameObject.Find("Qtext").GetComponent<TextMeshPro>();
    SetQuizText();
    SetChallengeBall();
    SetAllHalfed(true);
  }

  void SetQuizText()
  {
    qtext_tmp.SetText(questions[question]);
    for(int i = 0; i < 4; i++) option_tabs[i].tmp.SetText(options[question*4+i]);
  }

  void SetChallengeBall()
  {
    double volume      = ThermoMath.v_given_percent(Random.Range(0.1f,0.9f));
    double temperature = ThermoMath.t_given_percent(Random.Range(0.1f,0.9f));
    double pressure    = ThermoMath.p_given_vt(volume, temperature);
    challenge_dot.transform.localPosition = thermo.plot(pressure, volume, temperature);
  }

  void SetAllHalfed(bool h)
  {
    halfed = h;
    for(int i = 0; i < halfables.Count; i++)
      halfables[i].setHalf(halfed);
    //special case, only halfed when engaged
    if(!tool_coil.engaged) tool_coil.gameObject.GetComponent<Halfable>().setHalf(false);
    if(!tool_insulator.engaged) tool_insulator.gameObject.GetComponent<Halfable>().setHalf(false);
  }

  Vector3 popVector()
  {
    return new Vector3(Random.Range(-1f,1f),1f,Random.Range(-1f,1f));
  }

  // The three functions below are used to manage attach/detach and storage of tools.
  // Generally, they have to set the transforms properly, update state variables,
  // and update text.
  void ActivateTool(Tool t)
  {
    GameObject o = t.gameObject;
    o.transform.SetParent(t.active.transform);
    t.touchable.grabbed = false;
    t.engaged = true;
    t.stored = false;
    t.boxcollider.isTrigger = true;
    // Not sure the two below are ever used? We don't have dials for these tools in use.
    //     if(t == tool_insulator) t.dial_dial.val = (float)ThermoMath.percent_given_t(thermo.temperature);
    //else if(t == tool_clamp)     t.dial_dial.val = (float)ThermoMath.percent_given_v(thermo.volume);
    t.dial_dial.Reset(); // reset tool when we add it.
    t.textv_tmpro.SetText("{0:3}"+t.dial_dial.unit,(float)t.dial_dial.val);
    UpdateApplyTool(t);
    o.transform.localPosition = new Vector3(0f,0f,0f);
    o.transform.localRotation = Quaternion.identity;
    o.transform.localScale = new Vector3(1f,1f,1f);
    float v = t.active.transform.localScale.x; //can grab any dimension
    Vector3 invscale = new Vector3(1f/v,1f/v,1f/v);
    t.text.transform.localScale = invscale;
    Halfable h = o.GetComponent<Halfable>();
    if(h != null) h.setHalf(halfed); //conform to half-ness while engaged
  }
  void StoreTool(Tool t)
  {
    GameObject o = t.gameObject;
    o.transform.SetParent(t.storage.transform);
    t.touchable.grabbed = false;
    t.engaged = false;
    t.stored = true;
    o.transform.localPosition = new Vector3(0f,0f,0f);
    o.transform.localRotation = Quaternion.identity;
    o.transform.localScale = new Vector3(1f,1f,1f);
    float v = t.storage.transform.localScale.x; //can grab any dimension
    Vector3 invscale = new Vector3(1f/v,1f/v,1f/v);
    t.text.transform.localScale = invscale;
    Halfable h = o.GetComponent<Halfable>();
    if(h != null) h.setHalf(false); //Un-half when we store a tool.
    t.dial_dial.Reset(); // definitely need to reset tool when we store it.
    t.textv_tmpro.SetText("{0:3}"+t.dial_dial.unit,(float)t.dial_dial.val);
    UpdateApplyTool(t);
  }
  void DetachTool(Tool t, Vector3 vel)
  {
    GameObject o = t.gameObject;
    o.transform.SetParent(t.touchable.og_parent);
    t.touchable.grabbed = false;
    t.engaged = false;
    t.stored = false;
    o.transform.localScale = new Vector3(1f,1f,1f);
    t.text.transform.localScale = new Vector3(1f,1f,1f);
    t.rigidbody.isKinematic = false;
    t.rigidbody.velocity = vel;
    t.dial_dial.Reset(); // may as well reset tool when we remove it, too.
    t.textv_tmpro.SetText("{0:3}"+t.dial_dial.unit,(float)t.dial_dial.val);
    UpdateApplyTool(t);
  }

  /*
  tried during:
  - newly grabbed
  - newly snapped on
  - dial altered
  */
  void UpdateApplyTool(Tool t) //alters "applied_x"
  {
    if(t == tool_insulator)
    {
      //do nothing
      return;
    }
    else if(t == tool_burner || t == tool_coil)
    {
      if(tool_burner.engaged && tool_coil.engaged)
      {
        if(t == tool_burner) DetachTool(tool_coil,popVector());
        if(t == tool_coil)   DetachTool(tool_burner,popVector());
      }

      if(t == tool_burner)
      {
        var vel = flame.velocityOverLifetime;
        vel.speedModifierMultiplier = Mathf.Lerp(0.1f,0.5f,t.dial_dial.val);
      }
      else if(t == tool_coil)
      {
        //TODO: coil visuals?
      }

      applied_heat = 0;
      if(tool_burner.engaged) applied_heat += tool_burner.dial_dial.map;
      if(tool_coil.engaged)   applied_heat += tool_coil.dial_dial.map;
    }
    else if(t == tool_clamp)
    {
      //const float MAX_WIDTH = 2.0f;
      //const float TOP_BASE_Y_POS = 0.049f;
      //const float MID_BASE_Y_POS = 0.0164f;
      //var clamp_mid = GameObject.Find("clamp_mid");
      //var clamp_mid_mesh = clamp_mid.GetComponent<MeshFilter>().mesh;
      //var clamp_top = GameObject.Find("clamp_mid");
      //var clamp_top_mesh = clamp_top.GetComponent<MeshFilter>().mesh;
      //clamp_mid.transform.localScale = new Vector3(1.0f, 1.0f + MAX_WIDTH * t.dial_dial.val, 1.0f);
      //*clamp_mid.transform.localScale.y
      //clamp_mid.transform.position = new Vector3(clamp_mid.transform.position.x, MID_BASE_Y_POS + clamp_mid_mesh.bounds.extents.y, clamp_mid.transform.position.z);
      //clamp_top.transform.position = new Vector3(clamp_mid.transform.position.x, TOP_BASE_Y_POS + clamp_mid_mesh.bounds.extents.y, clamp_mid.transform.position.z);
      applied_weight = 0;
      if(tool_weight.engaged)  applied_weight += tool_weight.dial_dial.map;
      if(tool_balloon.engaged) applied_weight += tool_balloon.dial_dial.map;
    }
    else if(t == tool_weight || t == tool_balloon)
    {
      if(tool_weight.engaged && tool_balloon.engaged)
      {
             if(t == tool_weight)  DetachTool(tool_balloon,popVector());
        else if(t == tool_balloon) DetachTool(tool_weight,popVector());
      }

      float v = 1f;
           if(t == tool_weight)  v += tool_weight.dial_dial.val;
      else if(t == tool_balloon) v += tool_balloon.dial_dial.val;
      Vector3 scale;
      Vector3 invscale;

      scale = new Vector3(v,v,v);
      invscale = new Vector3(1f/v,1f/v,1f/v);
      t.active.transform.localScale = scale;
      if(t.engaged) t.text.transform.localScale = invscale;

      v *= t.default_storage_scale;
      scale = new Vector3(v,v,v);
      invscale = new Vector3(1f/v,1f/v,1f/v);
      t.storage.transform.localScale = scale;
      if(t.stored) t.text.transform.localScale = invscale;

      //math
      applied_weight = 0;
      if(tool_weight.engaged)  applied_weight += tool_weight.dial_dial.map;
      if(tool_balloon.engaged) applied_weight += tool_balloon.dial_dial.map;
    }

  }

  //safe to call if not interactable, as it will just do nothing
  void TryInteractable(GameObject actable, float x_val, float y_val, ref float r_x, ref float r_y)
  {
    //grabbing handle
    if(actable == handle_workspace)
    {
      float dy = (r_y-y_val);
      workspace.transform.position = new Vector3(workspace.transform.position.x,workspace.transform.position.y-dy,workspace.transform.position.z);
    }
    else
    {
      Dial d = actable.GetComponent<Dial>();
      //grabbing dial
      if(d != null)
      {
        Tool t = d.tool.GetComponent<Tool>();
        float dx = (r_x-x_val)*-10f;
        d.val = Mathf.Clamp(d.val-dx,0f,1f);

        UpdateApplyTool(t);
      }
    }
  }

  /*
   * This function seems to handle all possible interactions between the hand and other objects.
   * Honestly, I haven't quite got a full understanding of this ~200-line behemoth.
   */
  //"left_hand": true -> left, false -> right
  void TryHand(bool left_hand, float htrigger_val, float itrigger_val, float x_val, float y_val, Vector3 hand_vel, ref bool ref_htrigger, ref bool ref_itrigger, ref int ref_htrigger_delta, ref int ref_itrigger_delta, ref float ref_x, ref float ref_y, ref GameObject ref_hand, ref GameObject ref_grabbed, ref GameObject ref_ohand, ref GameObject ref_ograbbed)
  {
    float htrigger_threshhold = 0.1f;
    float itrigger_threshhold = 0.1f;

    //find deltas
    ref_htrigger_delta = 0;
    if(!ref_htrigger && htrigger_val > htrigger_threshhold)
    {
      ref_htrigger_delta = 1;
      ref_htrigger = true;
    }
    else if(ref_htrigger && htrigger_val <= htrigger_threshhold)
    {
      ref_htrigger_delta = -1;
      ref_htrigger = false;
    }

    ref_itrigger_delta = 0;
    if(!ref_itrigger && itrigger_val > itrigger_threshhold)
    {
      ref_itrigger_delta = 1;
      ref_itrigger = true;
      ref_x = x_val;
      ref_y = y_val;
    }
    else if(ref_itrigger && itrigger_val <= itrigger_threshhold)
    {
      ref_itrigger_delta = -1;
      ref_itrigger = false;
    }

    //find new grabs
    if(ref_grabbed == null && ref_htrigger_delta == 1)
    {
      //first try movables
      for(int i = 0; ref_grabbed == null && i < movables.Count; i++)
      {
        if( //object newly grabbed
           ( left_hand && movables[i].ltouch) ||
           (!left_hand && movables[i].rtouch)
          )
        {
          ref_grabbed = movables[i].gameObject;
          ref_grabbed.transform.SetParent(ref_hand.transform);
          if(ref_grabbed == ref_ograbbed) ref_ograbbed = null;
          movables[i].grabbed = true;
          Tool t = ref_grabbed.GetComponent<Tool>();
          if(t) //newly grabbed object is a tool
          {
            t.engaged = false;
            t.stored = false;
            ref_grabbed.transform.localScale = new Vector3(1f,1f,1f);
            t.text.transform.localScale = new Vector3(1f,1f,1f);
            t.rigidbody.isKinematic = true;
            t.boxcollider.isTrigger = false;
            UpdateApplyTool(t);
          }
          VisAid v = ref_grabbed.GetComponent<VisAid>();
          if(v) //newly grabbed object is a visaid
          {
            v.stored = false;
            v.rigidbody.isKinematic = true;
          }
        }
      }
      //then dials
      if(ref_grabbed == null)
      {
        for(int i = 0; i < tools.Count; i++)
        {
          if( //dial newly grabbed
             ( left_hand && tools[i].dial_touchable.ltouch) ||
             (!left_hand && tools[i].dial_touchable.rtouch)
            )
          {
            ref_grabbed = tools[i].dial;
            tools[i].dial_touchable.grabbed = true;
            if(ref_grabbed == ref_ograbbed) ref_ograbbed = null;
          }
        }
      }

      //then extraaneous
      if(ref_grabbed == null) //still not holding anything
      {
        Touchable g = handle_workspace_touchable;
        if( //handle newly grabbed
          ( left_hand && g.ltouch) ||
          (!left_hand && g.rtouch)
        )
        {
          ref_grabbed = handle_workspace;
          g.grabbed = true;
          if(ref_grabbed == ref_ograbbed) ref_ograbbed = null;
        }
      }

      if (ref_grabbed == null) //still not holding anything
      {
        Touchable g = halfer_touchable;
        if ( //halfing button newly grabbed
          (left_hand && g.ltouch) ||
          (!left_hand && g.rtouch)
        )
        {
          ref_grabbed = halfer;
          g.touch = true;
          if (ref_grabbed == ref_ograbbed) ref_ograbbed = null;
          SetAllHalfed(!halfed);
        }
      }

      if (ref_grabbed == null) //still not holding anything
      {
        Touchable g = reset_touchable;
        if( //reset button newly grabbed
          ( left_hand && g.ltouch) ||
          (!left_hand && g.rtouch)
        )
        {
          ref_grabbed = reset;
          g.touch = true;
          if(ref_grabbed == ref_ograbbed) ref_ograbbed = null;
          thermo.Reset();
        }
      }

      if(ref_grabbed != null) //something newly grabbed
      {
        Halfable h = ref_grabbed.GetComponent<Halfable>();
        if(h != null) h.setHalf(false); //nothing should be halfed while being grabbed
      }
    }
    //find new releases
    else if(ref_grabbed && ref_htrigger_delta == -1) //something newly released
    {
      Tool t = ref_grabbed.GetComponent<Tool>();
      if(t) //tool newly released
      {
        if(t.active_ghost.tintersect) //tool released making it active
        {
          ActivateTool(t);
        }
        else if(t.storage_ghost.tintersect) //tool released making it stored
        {
          StoreTool(t);
        }
        else //tool released nowhere special
        {
          DetachTool(t,hand_vel);
        }
      }
      else //newly released object is NOT a tool
      {
        ref_grabbed.transform.SetParent(ref_grabbed.GetComponent<Touchable>().og_parent); //ok to do, even with a dial
        VisAid v = ref_grabbed.GetComponent<VisAid>();
        if(v) //visaid newly released
        {
          v.rigidbody.isKinematic = false;
          v.rigidbody.velocity = hand_vel;
        }
      }

      ref_grabbed.GetComponent<Touchable>().grabbed = false;
      ref_grabbed = null;
    }

    if(ref_grabbed) TryInteractable(ref_grabbed, x_val, y_val, ref ref_x, ref ref_y);

    ref_x = x_val;
    ref_y = y_val;

    //centerer
    if(vrcenter_fingertoggleable.finger) //finger hitting vrcenter object
    {
      if( //we're currently checking the correct hand
        ( left_hand && vrcenter_fingertoggleable.lfinger) ||
        (!left_hand && vrcenter_fingertoggleable.rfinger)
      )
      { //reset center position
        vrcenter_backing_meshrenderer.material = tab_hisel;
        UnityEngine.XR.InputTracking.Recenter();
        OVRManager.display.RecenterPose();
        Vector3 pos = cam_offset.transform.localPosition-(cam_offset.transform.localPosition+ceye.transform.localPosition);
        pos.y = 0f;
        cam_offset.transform.localPosition = pos;
      }
      else vrcenter_backing_meshrenderer.material = tab_hi;
    }
    else vrcenter_backing_meshrenderer.material = tab_default;

  }

  /*
   * Function to update object materials/appearance in response to a "grab" event.
   */
  void UpdateGrabVis()
  {
    for(int i = 0; i < tools.Count; i++)
    {
      Tool t = tools[i];
      GameObject g = t.gameObject;

      if(lgrabbed == g || rgrabbed == g)
      {
        //active
        if(t.active_ghost.tintersect)
        {
          t.active_available_meshrenderer.enabled = false;
          t.active_snap_meshrenderer.enabled      = true;
        }
        else
        {
          t.active_available_meshrenderer.enabled = true;
          t.active_snap_meshrenderer.enabled      = false;
        }
        //storage
        if(t.storage_ghost.tintersect)
        {
          t.storage_available_meshrenderer.enabled = false;
          t.storage_snap_meshrenderer.enabled      = true;
        }
        else
        {
          t.storage_available_meshrenderer.enabled = true;
          t.storage_snap_meshrenderer.enabled      = false;
        }
      }
      else
      {
        t.active_snap_meshrenderer.enabled      = false;
        t.active_available_meshrenderer.enabled = false;
        t.storage_snap_meshrenderer.enabled      = false;
        t.storage_available_meshrenderer.enabled = false;
      }
    }

    Touchable gr;
    bool ltouch = false;
    bool rtouch = false;
    for(int i = 0; i < movables.Count; i++)
    {
      gr = movables[i];
      if(gr.ltouch) ltouch = true;
      if(gr.rtouch) rtouch = true;
    }
    for(int i = 0; i < tools.Count; i++)
    {
      gr = tools[i].dial_touchable;
      if(gr.ltouch) ltouch = true;
      if(gr.rtouch) rtouch = true;
    }
    gr = handle_workspace_touchable;
    if(gr.ltouch) ltouch = true;
    if(gr.rtouch) rtouch = true;
    Touchable press_gr = halfer_touchable;
    if(press_gr.ltouch) ltouch = true;
    if(press_gr.rtouch) rtouch = true;
    press_gr = reset_touchable;
    if(press_gr.ltouch) ltouch = true;
    if(press_gr.rtouch) rtouch = true;

         if(lgrabbed) lhand_meshrenderer.materials = hand_grabbings;
    else if(ltouch)   lhand_meshrenderer.materials = hand_touchings;
    else              lhand_meshrenderer.materials = hand_emptys;

         if(rgrabbed) rhand_meshrenderer.materials = hand_grabbings;
    else if(rtouch)   rhand_meshrenderer.materials = hand_touchings;
    else              rhand_meshrenderer.materials = hand_emptys;
  }

  //give it a list of fingertoggleables, and it manipulates them to act as a singularly-selectable list
  int reconcileDependentSelectables(int known, List<Tab> list)
  {
    int n_toggled = 0;
    Tab t;
    for(int i = 0; i < list.Count; i++)
    {
      t = list[i];
      if(t.fingertoggleable.on) n_toggled++;
    }

    if(n_toggled <= 1)
    {
      known = -1;
      for(int i = 0; i < list.Count; i++)
      {
        t = list[i];
        if(t.fingertoggleable.on) known = i;
      }
    }
    else //need conflict resolution!
    {
      known = -1;
      for(int i = 0; i < list.Count; i++)
      {
        t = list[i];
        if(t.fingertoggleable.on)
        {
          if(known == -1) known = i;
          else
          {
            //if only t is intersecting, prefer t
            if(t.fingertoggleable.finger && !list[known].fingertoggleable.finger)
            {
              list[known].fingertoggleable.on = false;
              known = i;
            }
            else //prefer previous (ignore t)
              t.fingertoggleable.on = false;
          }
        }
      }
    }

    return known;
  }

  void updateSelectableVis(int known, List<Tab> list)
  {
    Tab t;
    for(int i = 0; i < list.Count; i++)
    {
      t = list[i];
      if(known == i) t.backing_meshrenderer.material = tab_sel;
      else           t.backing_meshrenderer.material = tab_default;
    }
  }

  /*
   * Another behemoth, does frame-by-frame updates to state, appearances, transforms, etc.
   * Includes calls to TryHand, UpdateGrabVis, etc. as well as calls to ThermoState functions.
   * Basically, wraps calls to a bunch of other functions, and a hodgepodge of other random tasks,
   * as far as I can tell.
   */
  void Update()
  {
    //hands keep trying to run away- no idea why (this is a silly way to keep them still)
    lactualhand.transform.localPosition    = new Vector3(0f,0f,0f);
    lactualhand.transform.localEulerAngles = new Vector3(0f,0f,90f);
    ractualhand.transform.localPosition    = new Vector3(0f,0f,0f);
    ractualhand.transform.localEulerAngles = new Vector3(0f,0f,-90f);

    //apply thermo
    if(!tool_clamp.engaged)
    {
      double psi_to_pascal = 6894.76;
      double neutral_pressure = 14.6959; //~1atm in psi
      double weight_pressure = applied_weight/thermo.surfacearea_insqr; //psi
      weight_pressure += neutral_pressure;
      weight_pressure *= psi_to_pascal; //conversion from psi to pascal

      //treat "applied_weight" as target, and iterate toward it, rather than applying it additively
      //(technically, "should" add_pressure(...) with every delta of weight on the piston, but that would result in very jumpy nonsense movements. iterating toward a target smooths it out)
      double delta_pressure = (weight_pressure-thermo.pressure)*0.01; //1% of difference
      if(System.Math.Abs(delta_pressure) > 1)
      {
        // we only are applying pressure added/released if we are in gas region.
        // if this ever changes, adapt this "if" check as needed.
        if (thermo.region == 2)
        {
          arrows.Go(delta_pressure > 0.0f);
          arrows.SetFlow(delta_pressure);
        }
        if (tool_insulator.engaged) thermo.add_pressure_insulated(delta_pressure);
        else                       thermo.add_pressure_uninsulated(delta_pressure);
      }
      else if (arrows.running)
      {
        arrows.Stop();
      }
    }
    //if(tool_insulator.engaged && applied_heat != 0) //yes, "engaged" is correct. if insulator NOT engaged, then any heat added IMMEDIATELY dissipates
    if (applied_heat != 0)
    {
      double insulation_coefficient = tool_insulator.engaged ? 1.0f : CONTAINER_INSULATION_COEFFICIENT;
      double heat_joules = insulation_coefficient * applied_heat * (double)Time.deltaTime;
      if(tool_clamp.engaged) thermo.add_heat_constant_v(heat_joules);
      else                   thermo.add_heat_constant_p(heat_joules);
    }

    //running blended average of hand velocity (transfers this velocity on "release object" for consistent "throwing")
    lhand_vel += (lhand.transform.position-lhand_pos)/Time.deltaTime;
    lhand_vel *= 0.5f;
    lhand_pos = lhand.transform.position;

    rhand_vel += (rhand.transform.position-rhand_pos)/Time.deltaTime;
    rhand_vel *= 0.5f;
    rhand_pos = rhand.transform.position;

    //input
    //float lhandt  = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger);
    //float lindext = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger);
    //float rhandt  = OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger);
    //float rindext = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger);
    float lhandt  = OVRInput.Get(OVRInput.RawAxis1D.LHandTrigger);
    float lindext = OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger);
    float rhandt  = OVRInput.Get(OVRInput.RawAxis1D.RHandTrigger);
    float rindext = OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger);
    //index compatibility
    if (OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.LTouch))
    {
      lhandt = 1.0f;
    }
    if(OVRInput.Get(OVRInput.Button.One,OVRInput.Controller.RTouch))
    {
      rhandt = 1.0f;
    }
    lhandt += lindext;
    rhandt += rindext;
    if (lindext > 0.0f || rindext > 0.0f)
    {
      ;
    }
    //test effect of hands one at a time ("true" == "left hand", "false" == "right hand")
    TryHand(true,  lhandt, lindext, lhand.transform.position.x, lhand.transform.position.y, lhand_vel, ref lhtrigger, ref litrigger, ref lhtrigger_delta, ref litrigger_delta, ref lz, ref ly, ref lhand, ref lgrabbed, ref rhand, ref rgrabbed); //left hand
    TryHand(false, rhandt, rindext, rhand.transform.position.x, rhand.transform.position.y, rhand_vel, ref rhtrigger, ref ritrigger, ref rhtrigger_delta, ref ritrigger_delta, ref rz, ref ry, ref rhand, ref rgrabbed, ref lhand, ref lgrabbed); //right hand

    UpdateGrabVis();

    //clipboard
    int old_board_mode = board_mode;
    board_mode = reconcileDependentSelectables(board_mode, mode_tabs);
    if(board_mode == -1) board_mode = old_board_mode;
    updateSelectableVis(board_mode, mode_tabs);

    switch(board_mode)
    {
      case 0: //instructions
        if(!instructions_parent.activeSelf) instructions_parent.SetActive(true);
        if( challenge_parent.activeSelf)    challenge_parent.SetActive(   false);
        if( quiz_parent.activeSelf)         quiz_parent.SetActive(        false);
        break;
      case 1: //challenge
        if( instructions_parent.activeSelf) instructions_parent.SetActive(false);
        if(!challenge_parent.activeSelf)    challenge_parent.SetActive(   true);
        if( quiz_parent.activeSelf)         quiz_parent.SetActive(        false);

        if(challenge_ball_collide.win)
        {
          challenge_ball_collide.win = false;
          SetChallengeBall();
        }
        break;
      case 2: //quiz
        if( instructions_parent.activeSelf) instructions_parent.SetActive(false);
        if( challenge_parent.activeSelf)    challenge_parent.SetActive(   false);
        if(!quiz_parent.activeSelf)         quiz_parent.SetActive(        true);
        qselected = reconcileDependentSelectables(qselected, option_tabs);
        updateSelectableVis(qselected, option_tabs);
        if(qselected != -1) //answer selected, can be confirmed
        {
          if(qconfirm_tab.fingertoggleable.finger) qconfirm_tab.backing_meshrenderer.material = tab_hisel; //touching
          else                                     qconfirm_tab.backing_meshrenderer.material = tab_sel;   //not touching

          if(!qconfirmed && qconfirm_tab.fingertoggleable.finger) //newly hit
          {
            qconfirmed = true;
            if(qselected == answers[question])
              ; //correct
            else
              ; //incorrect
            givens[question] = qselected;
            question++;
            SetQuizText();
          }
        }
        else //no answer selected- can't confirm
        {
          if(qconfirm_tab.fingertoggleable.finger) qconfirm_tab.backing_meshrenderer.material = tab_hi;      //touching
          else                                     qconfirm_tab.backing_meshrenderer.material = tab_default; //not touching

          qconfirmed = false;
        }
        break;
      case 3: //congratulations
        board_mode = 2; //immediately return back to quiz until this section is actually implemented
        break;
    }

    //tooltext
    Tool t;
    for(int i = 0; i < tools.Count; i++)
    {
      t = tools[i];
      t.dial_dial.examined = false;
      if(t.dial == lgrabbed || t.dial == rgrabbed) t.dial_dial.examined = true;
      if(t.dial_dial.val != t.dial_dial.prev_val)
      {
        t.textv_tmpro.SetText("{0:3}"+t.dial_dial.unit,(float)t.dial_dial.map);
        t.dial_dial.examined = true;
      }
      t.dial_dial.prev_val = t.dial_dial.val;
    }

    // we did tool text above, so here I'll drop in the bit to check whether to show phase warning for balloon and weight.
    switch (thermo.region)
    {
      case 0:
      case 1:
        tool_weight.textn.GetComponent<MeshRenderer>().enabled = true;
        tool_weight.disabled = true;
        tool_balloon.textn.GetComponent<MeshRenderer>().enabled = true;
        tool_balloon.disabled = true;
        break;
      case 2:
        tool_weight.textn.GetComponent<MeshRenderer>().enabled = false;
        tool_weight.disabled = false;
        tool_balloon.textn.GetComponent<MeshRenderer>().enabled = false;
        tool_balloon.disabled = false;
        break;
    }

    for(int i = 0; i < tools.Count; i++)
    {
      t = tools[i];
      if(!t.text_fadable.stale)
      {
        if(t.text_fadable.alpha == 0f)
        {
          t.textv_meshrenderer.enabled = false;
          t.textl_meshrenderer.enabled = false;
        }
        else
        {
          t.textv_meshrenderer.enabled = true;
          t.textl_meshrenderer.enabled = true;
          Color32 c = t.disabled ? new Color32(70,70,70,(byte)(t.text_fadable.alpha*255)) : new Color32(0,0,0,(byte)(t.text_fadable.alpha*255));
          t.textv_tmpro.faceColor = c;
          t.textl_tmpro.faceColor = c;
        }
      }
    }
    thermo.UpdateErrorState();

  }

}

