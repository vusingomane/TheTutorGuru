import { Router, type IRouter } from "express";
import healthRouter from "./health";
import accountsRouter from "./accounts";
import classesRouter from "./classes";
import resourcesRouter from "./resources";

const router: IRouter = Router();

router.use(healthRouter);
router.use(accountsRouter);
router.use(classesRouter);
router.use(resourcesRouter);

export default router;
