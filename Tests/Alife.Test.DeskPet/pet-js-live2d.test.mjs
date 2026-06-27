import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import vm from "node:vm";

const petScriptPath = path.resolve(
    "sources",
    "Alife.DeskPet",
    "Alife.DeskPet.Client",
    "wwwroot",
    "pet.js");

function createElement(tagName = "DIV") {
    return {
        tagName,
        innerText: "",
        value: "",
        onclick: null,
        onkeydown: null,
        classList: {
            values: new Set(),
            add(value) {
                this.values.add(value);
            },
            remove(value) {
                this.values.delete(value);
            }
        },
        addEventListener() {
        },
        setPointerCapture() {
        },
        hasPointerCapture() {
            return false;
        },
        releasePointerCapture() {
        }
    };
}

function createPetHarness() {
    const messages = [];
    const animationFrames = [];
    const parameterValues = new Map([
        ["ParamAngleX", 0],
        ["ParamAngleY", 0],
        ["ParamAngleZ", 0],
        ["ParamBodyAngleX", 0],
        ["ParamEyeLOpen", 1],
        ["ParamEyeROpen", 1],
        ["ParamMouthOpenY", 0],
        ["ParamBreath", 0]
    ]);
    const parameterRanges = new Map([
        ["ParamAngleX", [-30, 30]],
        ["ParamAngleY", [-30, 30]],
        ["ParamAngleZ", [-30, 30]],
        ["ParamBodyAngleX", [-10, 10]],
        ["ParamEyeLOpen", [0, 1]]
    ]);
    const parameterWrites = [];
    let messageHandler = null;
    let frameId = 0;

    const coreModel = {
        setParameterValueById(id, value) {
            parameterValues.set(id, value);
            parameterWrites.push([id, value]);
        },
        getParameterIds() {
            return Array.from(parameterValues.keys());
        },
        getParameterValueById(id) {
            return parameterValues.get(id) ?? 0;
        },
        getParameterMinimumValueById(id) {
            return parameterRanges.get(id)?.[0] ?? -1;
        },
        getParameterMaximumValueById(id) {
            return parameterRanges.get(id)?.[1] ?? 1;
        },
        getDrawableIds() {
            return [];
        }
    };

    const live2dModel = {
        internalModel: {
            coreModel,
            originalHeight: 1000,
            focusController: {},
            getHitAreaDefs() {
                return [];
            }
        },
        height: 1000,
        scale: {
            y: 1,
            set(value) {
                this.y = value;
            }
        },
        position: {
            set(x, y) {
                this.x = x;
                this.y = y;
            }
        },
        anchor: {
            set(x, y) {
                this.x = x;
                this.y = y;
            }
        },
        interactive: false,
        expression() {
        },
        motion() {
        },
        focus() {
        },
        hitTest() {
            return [];
        }
    };

    const elements = new Map();
    const context = {
        console: {
            log() {
            },
            error() {
            }
        },
        document: {
            documentElement: {
                style: {
                    setProperty() {
                    }
                }
            },
            getElementById(id) {
                if (!elements.has(id)) {
                    elements.set(id, createElement(id === "canvas" ? "CANVAS" : "DIV"));
                }
                return elements.get(id);
            }
        },
        window: {
            innerHeight: 540,
            innerWidth: 960,
            addEventListener() {
            },
            chrome: {
                webview: {
                    addEventListener(type, handler) {
                        if (type === "message") {
                            messageHandler = handler;
                        }
                    },
                    postMessage(data) {
                        messages.push(data);
                    }
                }
            }
        },
        PIXI: {
            Application: class {
                constructor() {
                    this.stage = {
                        addChild() {
                        },
                        removeChild() {
                        }
                    };
                }
            },
            live2d: {
                MotionPriority: {
                    FORCE: 3
                },
                Live2DModel: {
                    async from() {
                        return live2dModel;
                    }
                }
            }
        },
        Date,
        Math,
        Set,
        Map,
        Object,
        JSON,
        requestAnimationFrame(callback) {
            frameId += 1;
            animationFrames.push({ id: frameId, callback });
            return frameId;
        },
        cancelAnimationFrame(id) {
            const index = animationFrames.findIndex(frame => frame.id === id);
            if (index >= 0) {
                animationFrames.splice(index, 1);
            }
        }
    };

    context.window.requestAnimationFrame = context.requestAnimationFrame;
    context.window.cancelAnimationFrame = context.cancelAnimationFrame;

    vm.runInNewContext(fs.readFileSync(petScriptPath, "utf8"), context, {
        filename: petScriptPath
    });

    async function send(data) {
        assert.equal(typeof messageHandler, "function");
        messageHandler({ data });
        await new Promise(resolve => setImmediate(resolve));
    }

    function runNextAnimationFrame() {
        const frame = animationFrames.shift();
        assert.ok(frame, "expected an animation frame to be scheduled");
        frame.callback(Date.now());
    }

    return {
        messages,
        parameterValues,
        parameterWrites,
        animationFrames,
        send,
        runNextAnimationFrame
    };
}

test("pet.js applies single and batched Live2D parameters", async () => {
    const pet = createPetHarness();

    await pet.send({ type: "load", url: "model.model3.json" });
    await pet.send({ type: "param", id: "ParamAngleX", value: 15 });
    await pet.send({
        type: "params",
        params: {
            ParamAngleY: -8,
            ParamEyeLOpen: 0.5
        }
    });

    assert.equal(pet.parameterValues.get("ParamAngleX"), 15);
    assert.equal(pet.parameterValues.get("ParamAngleY"), -8);
    assert.equal(pet.parameterValues.get("ParamEyeLOpen"), 0.5);
});

test("pet.js clamps lip sync and reports parameter metadata", async () => {
    const pet = createPetHarness();

    await pet.send({ type: "load", url: "model.model3.json" });
    await pet.send({ type: "lip-sync", value: 2 });
    await pet.send({ type: "get-params" });

    assert.equal(pet.parameterValues.get("ParamMouthOpenY"), 1);
    assert.deepEqual(
        JSON.parse(JSON.stringify(pet.messages.find(message => message.type === "params-list")?.params.ParamAngleX)),
        { value: 0, min: -30, max: 30 });
});

test("pet.js starts and stops the idle animation loop", async () => {
    const pet = createPetHarness();

    await pet.send({ type: "load", url: "model.model3.json" });
    assert.ok(pet.animationFrames.length > 0);

    pet.runNextAnimationFrame();
    assert.ok(pet.parameterWrites.some(([id]) => id === "ParamBreath"));
    assert.ok(pet.parameterWrites.some(([id]) => id === "ParamAngleZ"));

    await pet.send({ type: "idle-cycle", enabled: false });
    assert.equal(pet.animationFrames.length, 0);
});
