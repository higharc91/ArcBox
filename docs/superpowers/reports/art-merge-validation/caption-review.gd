extends SceneTree

func _initialize():
    var base = OS.get_environment("ARCBOX_REVIEW_ROOT")
    var report = base + "/docs/superpowers/reports/art-merge-validation/"
    var data = JSON.parse_string(FileAccess.get_file_as_string(report + "caption-input.json"))
    var font = FontFile.new()
    assert(font.load_dynamic_font(base + "/src/ArcBox.Host/fonts/TitilliumWeb-SemiBold.ttf") == OK)
    var widths = []
    for text in data.rows:
        var width = font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, 30).x
        assert(width <= 1162, "Narrator caption exceeds its real text area")
        widths.append(width)
    assert(data.rows.size() == 3)
    assert(" ".join(data.rows).replace("’", "'") == data.line.text)
    var background = ColorRect.new()
    background.color = Color("111b2a")
    background.size = Vector2(1920, 1080)
    root.add_child(background)
    var title = Label.new()
    title.text = "Narrator layout proof / unchanged 30px font / complete recorded wording"
    title.position = Vector2(36, 40)
    title.add_theme_font_override("font", font)
    title.add_theme_font_size_override("font_size", 30)
    root.add_child(title)
    var image = Image.load_from_file(base + "/content/tactics/base/ui/panels.png")
    image.resize(image.get_width()*2,image.get_height()*2,Image.INTERPOLATE_NEAREST)
    var box = StyleBoxTexture.new()
    box.texture = ImageTexture.create_from_image(image)
    var piece = data.panel
    box.region_rect = Rect2(piece.rect[0]*2,piece.rect[1]*2,piece.rect[2]*2,piece.rect[3]*2)
    for side in range(4):
        box.set_texture_margin(side,piece.slice[side]*2)
        box.set_expand_margin(side,piece.outset[side]*2)
        box.set_content_margin(side,piece.padding[side]*2+(6 if side in [0,2] else 0))
    var panel = PanelContainer.new()
    panel.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
    panel.add_theme_stylebox_override("panel",box)
    var margin = MarginContainer.new()
    for side in ["left","right"]: margin.add_theme_constant_override("margin_"+side,10)
    margin.add_theme_constant_override("margin_top",14)
    margin.add_theme_constant_override("margin_bottom",10)
    var inside = HBoxContainer.new()
    inside.custom_minimum_size = Vector2(0,180)
    var rows = VBoxContainer.new()
    rows.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    rows.size_flags_vertical = Control.SIZE_SHRINK_CENTER
    rows.add_theme_constant_override("separation",2)
    for text in data.rows:
        var row = Label.new()
        row.text = text
        row.add_theme_font_override("font",font)
        row.add_theme_font_size_override("font_size",30)
        row.add_theme_color_override("font_color",Color("bdb093"))
        rows.add_child(row)
    inside.add_child(rows)
    margin.add_child(inside)
    panel.add_child(margin)
    root.add_child(panel)
    await process_frame
    panel.size = Vector2(1230,panel.get_combined_minimum_size().y)
    panel.position = Vector2(36,1080-36-panel.size.y)
    await create_timer(0.5).timeout
    await RenderingServer.frame_post_draw
    assert(panel.position.y >= 0)
    for row in rows.get_children():
        assert(panel.get_global_rect().encloses(row.get_global_rect()))
    assert(root.get_texture().get_image().save_png(report+"native-caption.png") == OK)
    var proof = {"fontSize":30,"rows":3,"availableTextWidth":1162,"actualRowWidths":widths,"panel":str(panel.get_global_rect()),"allRowsInsidePanel":true,"wordingPreserved":true,"kind":"native font/layout harness; not gameplay or a physical-TV test"}
    var file = FileAccess.open(report+"native-caption.json",FileAccess.WRITE)
    file.store_string(JSON.stringify(proof,"  ")+"\n")
    print("CAPTION REVIEW: ",JSON.stringify(proof))
    quit()
